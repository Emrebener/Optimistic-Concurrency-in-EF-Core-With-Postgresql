using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OptimisticConcurrencyDemo.Models;

namespace OptimisticConcurrencyDemo.Data;

public class ConcurrencyInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        BumpVersions(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        BumpVersions(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void BumpVersions(DbContextEventData eventData)
    {
        if (eventData.Context is null) return;

        foreach (var entry in eventData.Context.ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                entry.Entity.Version = Guid.NewGuid();
            }
        }
    }
}
