using System;
using Marten.Metadata;

namespace Marten.EntityFrameworkCore.Tests;

// Events for testing projections
public record OrderPlaced(Guid OrderId, string CustomerName, decimal Amount, int Items);
public record OrderShipped(Guid OrderId);
public record OrderCancelled(Guid OrderId);

// Marten aggregate document
public class Order
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int ItemCount { get; set; }
    public bool IsShipped { get; set; }
    public bool IsCancelled { get; set; }
}

// Tenanted aggregate document for conjoined multi-tenancy tests
public class TenantedOrder: ITenanted
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int ItemCount { get; set; }
    public bool IsShipped { get; set; }
    public bool IsCancelled { get; set; }
    public string? TenantId { get; set; }
}
