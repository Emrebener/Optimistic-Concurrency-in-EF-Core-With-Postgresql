# Optimistic concurrency in EF Core + PostgreSQL

A minimal ASP.NET Core API on .NET 10 demonstrating project-wide optimistic concurrency across an EF Core schema, against PostgreSQL via Npgsql.

Companion project for the blog post [Optimistic Locking in PostgreSQL via EF Core](https://emrebener.com/topics/dotnet/optimistic-locking-in-postgresql-via-ef-core/). The post walks the reasoning, the trade-offs, and the path from "no protection" to the setup in this repo. This repo is the canonical reference setup.

## Quick start

```bash
docker compose up -d
curl http://localhost:8081/api/coupons
```

The API container picks up Postgres on the docker network, runs migrations on startup (in `Development`), and serves on host port 8081. Postgres maps to host port 5433.

## API surface

Two entities, both inheriting the shared `Entity` base class. CRUD plus a redemption action on coupons:

```
GET    /api/coupons
GET    /api/coupons/{id}
POST   /api/coupons
PUT    /api/coupons/{id}
DELETE /api/coupons/{id}
POST   /api/coupons/{id}/redemptions     # demonstrates the retry wrapper

GET    /api/promotions
GET    /api/promotions/{id}
POST   /api/promotions
PUT    /api/promotions/{id}
DELETE /api/promotions/{id}
```

## The concurrency infrastructure

Six pieces compose into a project-wide optimistic-concurrency setup. Every new entity from now on inherits `Entity` and gets the same treatment automatically.

### 1. The `Entity` base class

Every persistable entity inherits from this. `Version` is the concurrency token; `Id` is the primary key by convention.

```csharp
// Models/Entity.cs
public abstract class Entity
{
    public Guid Id { get; set; }
    public Guid Version { get; set; }
}
```

`Coupon` and `Promotion` both inherit `Entity` and add their own fields. No `Id` or `Version` declarations are needed in the derived class.

### 2. The model-walk in `OnModelCreating`

Marks every `Entity`-derived type's `Version` as a concurrency token. One loop at model-finalisation time:

```csharp
// Data/AppDbContext.cs
foreach (var entityType in modelBuilder.Model.GetEntityTypes()
    .Where(et => typeof(Entity).IsAssignableFrom(et.ClrType)))
{
    modelBuilder.Entity(entityType.ClrType)
        .Property(nameof(Entity.Version))
        .IsConcurrencyToken();
}
```

Adding a new entity that inherits `Entity` requires zero changes here.

### 3. The `SaveChangesInterceptor`

EF Core's change tracker has no opinion about your domain's concurrency-token rule. The interceptor walks every `Entity`-derived tracked entry that's `Added` or `Modified` and assigns a fresh `Guid` to `Version` before SQL is generated:

```csharp
// Data/ConcurrencyInterceptor.cs
foreach (var entry in eventData.Context.ChangeTracker.Entries<Entity>())
{
    if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
    {
        entry.Entity.Version = Guid.NewGuid();
    }
}
```

Registered as a singleton and attached via `AddInterceptors`:

```csharp
// Program.cs
builder.Services.AddSingleton<ConcurrencyInterceptor>();
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("Postgres"))
        .AddInterceptors(sp.GetRequiredService<ConcurrencyInterceptor>());
});
```

### 4. The load helper

The `OriginalValue` override that routes the client's claimed version into EF is a one-liner that's easy to forget per handler. A small `DbContext` extension fuses the load with the override so every write handler gets it for free:

```csharp
// Data/DbContextLoadExtensions.cs
public static async Task<T?> LoadForUpdateAsync<T>(
    this DbContext db,
    Guid id,
    Guid claimedVersion,
    CancellationToken ct = default)
    where T : Entity
{
    var entity = await db.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct);
    if (entity is null) return null;
    db.Entry(entity).Property(e => e.Version).OriginalValue = claimedVersion;
    return entity;
}
```

Used in every PUT:

```csharp
var coupon = await db.LoadForUpdateAsync<Coupon>(id, request.Version, ct);
if (coupon is null) return NotFound();
// apply fields...
await db.SaveChangesAsync(ct);
```

Forgetting `LoadForUpdateAsync` means forgetting to load the entity at all, which fails on the next line. The "loaded but skipped the override" footgun is structurally impossible. An action filter or model binder could hide the override too, but routing the version through HTTP infrastructure couples the optimistic-concurrency mechanism to the request lifecycle; the helper sits at the data layer instead and works the same from background jobs or hosted services.

### 5. The retry wrapper

`DbUpdateConcurrencyException` carries the conflicting entries in `.Entries`. `entry.ReloadAsync()` refreshes each one from the database. The wrapper combines that with jittered exponential backoff and a max-attempts cap:

```csharp
// Data/DbContextRetryExtensions.cs
for (var attempt = 1; ; attempt++)
{
    try { return await operation(); }
    catch (DbUpdateConcurrencyException ex) when (attempt < policy.MaxAttempts)
    {
        foreach (var entry in ex.Entries) await entry.ReloadAsync(ct);
        await Task.Delay(JitteredBackoff(policy.InitialBackoff, attempt), ct);
    }
}
```

Use the wrapper for operations that are *idempotent under reload*: read fresh state, recompute the change, save again. The redemption endpoint is the canonical example:

```csharp
// Controllers/CouponsController.cs
[HttpPost("{id:guid}/redemptions")]
public async Task<IActionResult> Redeem(Guid id, CancellationToken ct)
{
    var outcome = await db.ExecuteWithConcurrencyRetryAsync(async () =>
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (coupon is null) return RedeemOutcome.NotFound;
        if (coupon.RedemptionsRemaining <= 0) return RedeemOutcome.Exhausted;

        coupon.RedemptionsRemaining -= 1;
        await db.SaveChangesAsync(ct);
        return RedeemOutcome.Ok;
    }, ct: ct);
    // ... map outcome to HTTP response
}
```

The default policy is three attempts with a 50ms initial backoff. Tune via `ConcurrencyRetryPolicy` per call site under heavy contention.

### 6. The exception filter

Conflicts that retry can't resolve, plus conflicts on user-driven updates where retry isn't appropriate (the `PUT` handler), bubble out as `DbUpdateConcurrencyException`. A global exception filter translates them into HTTP 409 with a `ProblemDetails` body:

```csharp
// Filters/ConcurrencyConflictExceptionFilter.cs
if (context.Exception is not DbUpdateConcurrencyException) return;
context.Result = new ConflictObjectResult(new ProblemDetails
{
    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
    Title = "Conflict",
    Status = StatusCodes.Status409Conflict,
    Detail = "The resource was modified by another caller. Refetch and retry.",
});
context.ExceptionHandled = true;
```

Registered globally:

```csharp
// Program.cs
builder.Services.AddControllers(options =>
{
    options.Filters.Add<ConcurrencyConflictExceptionFilter>();
});
```

`context.ExceptionHandled = true` is the load-bearing line. Without it, ASP.NET Core re-throws the exception after the filter returns.

## The `PUT` handler pattern

For user-driven updates where the client edits a stable snapshot of the row and submits a full body, the concurrency check has to compare against the version the client *read*, not whatever the database has *now*. `LoadForUpdateAsync` (above) takes the claimed version from the request and routes it into EF's `OriginalValue` slot at load time. The handler itself doesn't reference `OriginalValue`:

```csharp
var coupon = await db.LoadForUpdateAsync<Coupon>(id, request.Version, ct);
if (coupon is null) return NotFound();

coupon.Code = request.Code;
// ...
await db.SaveChangesAsync(ct);
```

Without that routing, EF would compare against the value it just loaded from the database, which always matches trivially, and the optimistic check would be a no-op.

The `PUT` endpoint doesn't use the retry wrapper. The client claimed a specific version; if it has moved, the user has to decide whether to merge or cancel. The exception filter converts the resulting `DbUpdateConcurrencyException` into a 409 and the caller takes it from there.

## Adding a new entity

The point of the base class plus interceptor is that the next entity is one declaration:

```csharp
public class Subscription : Entity
{
    public string PlanId { get; set; } = "";
    public DateTimeOffset RenewsAt { get; set; }
}
```

Plus a `DbSet`:

```csharp
// Data/AppDbContext.cs
public DbSet<Subscription> Subscriptions => Set<Subscription>();
```

The model-walk picks up `Subscription.Version` and marks it as a concurrency token. The interceptor's `Entries<Entity>()` filter bumps it on every save. The exception filter handles its conflicts. The controller's PUT calls `LoadForUpdateAsync<Subscription>(id, request.Version, ct)`; the helper is generic over `T : Entity` and works without per-entity registration. The controller is the only Subscription-specific code.

## Why an explicit `Version`, not `xmin`?

PostgreSQL has a hidden `xmin` system column that auto-advances on every row update. Combined with `IsRowVersion()` on a `uint` property, EF picks it up via an Npgsql convention and gets free concurrency control with no schema column. The blog post covers this in §3.

This project uses the explicit `Version uuid` column instead because:

- The setup is portable across providers (SQL Server, SQLite, etc.).
- The token is visible in `\d coupons` and to every schema-comparison tool.
- The schema acts as documentation. A reader of `coupons` sees `Version uuid` and knows the table participates in optimistic concurrency.

Trade-off: the application maintains the token via the interceptor, which is one extra class. For most production systems the trade is worth it. For a PostgreSQL-only project with no future provider plans, `xmin` is the smaller-surface choice.

## Layout

```
.
├── Controllers/
│   ├── CouponsController.cs            # CRUD + Redeem (uses retry wrapper)
│   └── PromotionsController.cs         # CRUD (proves the inheritance pattern)
├── Data/
│   ├── AppDbContext.cs                 # DbContext + IsConcurrencyToken model-walk
│   ├── ConcurrencyInterceptor.cs       # bumps Version on Added/Modified
│   ├── ConcurrencyRetryPolicy.cs       # MaxAttempts + InitialBackoff record
│   ├── DbContextLoadExtensions.cs      # LoadForUpdateAsync helper
│   └── DbContextRetryExtensions.cs     # ExecuteWithConcurrencyRetryAsync wrapper
├── Filters/
│   └── ConcurrencyConflictExceptionFilter.cs   # DbUpdateConcurrencyException → 409
├── Migrations/                          # EF Core migrations
├── Models/
│   ├── Entity.cs                       # base class with Id + Version
│   ├── Coupon.cs                       # : Entity
│   └── Promotion.cs                    # : Entity
├── Program.cs                          # DI registrations; MigrateAsync on startup in Development
├── Dockerfile
├── docker-compose.yml                  # API + Postgres
└── appsettings*.json
```

## Stack

- ASP.NET Core 10 (`net10.0`)
- Entity Framework Core 10 with Npgsql provider 10.0.x
- PostgreSQL 17 (via docker compose)
