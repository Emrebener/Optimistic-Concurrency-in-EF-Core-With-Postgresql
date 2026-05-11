using Microsoft.EntityFrameworkCore;
using OptimisticConcurrencyDemo.Models;

namespace OptimisticConcurrencyDemo.Data;

public static class DbContextLoadExtensions
{
    /// <summary>
    /// Loads an Entity-derived row by id and routes the client's claimed version
    /// into EF's <c>OriginalValue</c> for the concurrency check. Returns null if
    /// no row matches the id; SaveChanges throws DbUpdateConcurrencyException if
    /// the database has moved past the claimed version.
    /// </summary>
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
}
