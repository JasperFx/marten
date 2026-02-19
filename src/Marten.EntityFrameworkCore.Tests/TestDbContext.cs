using System;
using Microsoft.EntityFrameworkCore;

namespace Marten.EntityFrameworkCore.Tests;

public class OrderSummary
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int ItemCount { get; set; }
    public string Status { get; set; } = "Pending";
}

public class TestDbContext: DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options): base(options)
    {
    }

    public DbSet<OrderSummary> OrderSummaries => Set<OrderSummary>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderSummary>(entity =>
        {
            entity.ToTable("ef_order_summaries");
            entity.HasKey(e => e.Id);
        });
    }
}
