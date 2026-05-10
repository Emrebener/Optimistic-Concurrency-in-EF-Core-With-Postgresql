using Microsoft.EntityFrameworkCore;
using OptimisticConcurrencyDemo.Models;

namespace OptimisticConcurrencyDemo.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Promotion> Promotions => Set<Promotion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Coupon>(b =>
        {
            b.HasIndex(c => c.Code).IsUnique();
            b.Property(c => c.Code).IsRequired().HasMaxLength(64);
            b.Property(c => c.Description).HasMaxLength(512);
        });

        modelBuilder.Entity<Promotion>(b =>
        {
            b.Property(p => p.Name).IsRequired().HasMaxLength(128);
        });

        // Mark Version as a concurrency token on every Entity-derived type.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
            .Where(et => typeof(Entity).IsAssignableFrom(et.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType)
                .Property(nameof(Entity.Version))
                .IsConcurrencyToken();
        }
    }
}
