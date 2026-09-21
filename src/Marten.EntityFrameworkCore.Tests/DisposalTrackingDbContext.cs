using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Marten.EntityFrameworkCore.Tests;

public class TrackedOrder
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
}

/// <summary>
/// A <see cref="DbContext"/> that counts its own construction and disposal, so #5457 can be pinned
/// on the thing that is actually wrong -- a context created per daemon batch and never disposed --
/// rather than on a backend count, which depends on whether the context happens to own its
/// connection and on how quickly the pool reclaims one that it does not.
/// </summary>
public class DisposalTrackingDbContext: DbContext
{
    private static int _created;
    private static int _disposed;

    public DisposalTrackingDbContext(DbContextOptions<DisposalTrackingDbContext> options): base(options)
    {
        Interlocked.Increment(ref _created);
    }

    public static int Created => Volatile.Read(ref _created);
    public static int Disposed => Volatile.Read(ref _disposed);

    public static void ResetCounts()
    {
        Interlocked.Exchange(ref _created, 0);
        Interlocked.Exchange(ref _disposed, 0);
    }

    public DbSet<TrackedOrder> TrackedOrders => Set<TrackedOrder>();

    public override void Dispose()
    {
        Interlocked.Increment(ref _disposed);
        base.Dispose();
    }

    public override ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposed);
        return base.DisposeAsync();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrackedOrder>(entity =>
        {
            entity.ToTable("ef_tracked_orders");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CustomerName).HasColumnName("customer_name");
            entity.Property(e => e.TotalAmount).HasColumnName("total_amount");
        });
    }
}
