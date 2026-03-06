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

public class OrderDetail
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int ItemCount { get; set; }
    public bool IsShipped { get; set; }
    public string Status { get; set; } = "Unknown";
}

public class TestDbContext: DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options): base(options)
    {
    }

    public DbSet<OrderSummary> OrderSummaries => Set<OrderSummary>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<CustomerOrderHistory> CustomerOrderHistories => Set<CustomerOrderHistory>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderSummary>(entity =>
        {
            entity.ToTable("ef_order_summaries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CustomerName).HasColumnName("customer_name");
            entity.Property(e => e.TotalAmount).HasColumnName("total_amount");
            entity.Property(e => e.ItemCount).HasColumnName("item_count");
            entity.Property(e => e.Status).HasColumnName("status");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("ef_orders");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CustomerName).HasColumnName("customer_name");
            entity.Property(e => e.TotalAmount).HasColumnName("total_amount");
            entity.Property(e => e.ItemCount).HasColumnName("item_count");
            entity.Property(e => e.IsShipped).HasColumnName("is_shipped");
            entity.Property(e => e.IsCancelled).HasColumnName("is_cancelled");
        });

        modelBuilder.Entity<CustomerOrderHistory>(entity =>
        {
            entity.ToTable("ef_customer_order_histories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TotalOrders).HasColumnName("total_orders");
            entity.Property(e => e.TotalSpent).HasColumnName("total_spent");
        });
    }
}

public class TenantedTestDbContext: DbContext
{
    public TenantedTestDbContext(DbContextOptions<TenantedTestDbContext> options): base(options)
    {
    }

    public DbSet<TenantedOrder> TenantedOrders => Set<TenantedOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantedOrder>(entity =>
        {
            entity.ToTable("ef_tenanted_orders");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CustomerName).HasColumnName("customer_name");
            entity.Property(e => e.TotalAmount).HasColumnName("total_amount");
            entity.Property(e => e.ItemCount).HasColumnName("item_count");
            entity.Property(e => e.IsShipped).HasColumnName("is_shipped");
            entity.Property(e => e.IsCancelled).HasColumnName("is_cancelled");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
        });
    }
}
