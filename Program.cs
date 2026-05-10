using Microsoft.EntityFrameworkCore;
using OptimisticConcurrencyDemo.Data;
using OptimisticConcurrencyDemo.Filters;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ConcurrencyConflictExceptionFilter>();
});
builder.Services.AddOpenApi();

builder.Services.AddSingleton<ConcurrencyInterceptor>();
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("Postgres"))
        .AddInterceptors(sp.GetRequiredService<ConcurrencyInterceptor>());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseAuthorization();
app.MapControllers();

app.Run();
