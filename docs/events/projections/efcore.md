# EF Core Projections

Marten provides first-class support for projecting events into Entity Framework Core `DbContext` entities. This lets you use EF Core's model configuration, change tracking, and migration tooling while still benefiting from Marten's event sourcing infrastructure.

The `Marten.EntityFrameworkCore` NuGet package provides three projection base classes:

| Base Class | Use Case |
| ------------ | ---------- |
| `EfCoreSingleStreamProjection<TDoc, TId, TDbContext>` | Aggregate a single event stream into one EF Core entity |
| `EfCoreMultiStreamProjection<TDoc, TId, TDbContext>` | Aggregate events across multiple streams into one EF Core entity |
| `EfCoreEventProjection<TDbContext>` | React to individual events, writing to both EF Core and Marten |

All three types support **Inline**, **Async**, and **Live** projection lifecycles.

## Installation

Add the `Marten.EntityFrameworkCore` NuGet package to your project:

```bash
dotnet add package Marten.EntityFrameworkCore
```

## Defining a DbContext

EF Core projections require a `DbContext` with entity mappings. Use `OnModelCreating` to configure table names and column mappings:

```csharp
public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderSummary> OrderSummaries => Set<OrderSummary>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("ef_orders");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CustomerName).HasColumnName("customer_name");
            entity.Property(e => e.TotalAmount).HasColumnName("total_amount");
            entity.Property(e => e.ItemCount).HasColumnName("item_count");
            entity.Property(e => e.IsShipped).HasColumnName("is_shipped");
        });
    }
}
```

::: tip
Entity tables defined in the DbContext are automatically migrated alongside Marten's own schema objects through [Weasel](https://weasel.jasperfx.net/). You do not need to run `dotnet ef database update` separately.
:::

## Single Stream Projections

Use `EfCoreSingleStreamProjection<TDoc, TId, TDbContext>` to build an aggregate from a single event stream and persist it through EF Core.

### Entity and Events

```csharp
// Events
public record OrderPlaced(Guid OrderId, string CustomerName, decimal Amount, int Items);
public record OrderShipped(Guid OrderId);
public record OrderCancelled(Guid OrderId);

// EF Core entity (the aggregate)
public class Order
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int ItemCount { get; set; }
    public bool IsShipped { get; set; }
    public bool IsCancelled { get; set; }
}
```

### Projection Class

Override `ApplyEvent` to handle each event. The `DbContext` is available for querying or writing side effects:

```csharp
public class OrderAggregate
    : EfCoreSingleStreamProjection<Order, Guid, OrderDbContext>
{
    public override Order? ApplyEvent(
        Order? snapshot, Guid identity, IEvent @event,
        OrderDbContext dbContext, IQuerySession session)
    {
        switch (@event.Data)
        {
            case OrderPlaced placed:
                return new Order
                {
                    Id = placed.OrderId,
                    CustomerName = placed.CustomerName,
                    TotalAmount = placed.Amount,
                    ItemCount = placed.Items
                };

            case OrderShipped:
                if (snapshot != null) snapshot.IsShipped = true;
                return snapshot;

            case OrderCancelled:
                if (snapshot != null) snapshot.IsCancelled = true;
                return snapshot;
        }

        return snapshot;
    }
}
```

### Registration

Use the `StoreOptions.Add()` extension method to register the projection. This sets up EF Core storage, Weasel schema migration, and the projection lifecycle in one call:

```csharp
var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);
    opts.Add(new OrderAggregate(), ProjectionLifecycle.Inline);
});
```

## Multi Stream Projections

Use `EfCoreMultiStreamProjection<TDoc, TId, TDbContext>` to aggregate events from multiple streams into a single EF Core entity.

### Entity and Events

```csharp
public record CustomerOrderPlaced(Guid OrderId, string CustomerName, decimal Amount);
public record CustomerOrderCompleted(Guid OrderId, string CustomerName);

public class CustomerOrderHistory
{
    public string Id { get; set; } = string.Empty;
    public int TotalOrders { get; set; }
    public decimal TotalSpent { get; set; }
}
```

### Projection Class

Use the constructor to configure event-to-aggregate identity mapping, then override `ApplyEvent`:

```csharp
public class CustomerOrderHistoryProjection
    : EfCoreMultiStreamProjection<CustomerOrderHistory, string, OrderDbContext>
{
    public CustomerOrderHistoryProjection()
    {
        // Map events to the aggregate identity (customer name in this case)
        Identity<CustomerOrderPlaced>(e => e.CustomerName);
        Identity<CustomerOrderCompleted>(e => e.CustomerName);
    }

    public override CustomerOrderHistory? ApplyEvent(
        CustomerOrderHistory? snapshot, string identity,
        IEvent @event, OrderDbContext dbContext)
    {
        snapshot ??= new CustomerOrderHistory { Id = identity };

        switch (@event.Data)
        {
            case CustomerOrderPlaced placed:
                snapshot.TotalOrders++;
                snapshot.TotalSpent += placed.Amount;
                break;
        }

        return snapshot;
    }
}
```

### Registration

```csharp
var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);
    opts.Events.StreamIdentity = StreamIdentity.AsString;
    opts.Add(new CustomerOrderHistoryProjection(), ProjectionLifecycle.Async);
});
```

## Event Projections

Use `EfCoreEventProjection<TDbContext>` when you need to react to individual events and write to both EF Core entities and Marten documents in the same transaction:

### Projection Class

```csharp
public class OrderSummaryProjection : EfCoreEventProjection<OrderDbContext>
{
    protected override async Task ProjectAsync(
        IEvent @event, OrderDbContext dbContext,
        IDocumentOperations operations, CancellationToken token)
    {
        switch (@event.Data)
        {
            case OrderPlaced placed:
                // Write to EF Core
                dbContext.OrderSummaries.Add(new OrderSummary
                {
                    Id = placed.OrderId,
                    CustomerName = placed.CustomerName,
                    TotalAmount = placed.Amount,
                    ItemCount = placed.Items,
                    Status = "Placed"
                });

                // Also write to Marten
                operations.Store(new Order
                {
                    Id = placed.OrderId,
                    CustomerName = placed.CustomerName,
                    TotalAmount = placed.Amount,
                    ItemCount = placed.Items
                });
                break;

            case OrderShipped shipped:
                var summary = await dbContext.OrderSummaries
                    .FindAsync(new object[] { shipped.OrderId }, token);
                if (summary != null)
                {
                    summary.Status = "Shipped";
                }
                break;
        }
    }
}
```

### Registration

`EfCoreEventProjection` uses the standard `Projections.Add()` method with a separate call to register entity tables:

```csharp
var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);
    opts.Projections.Add(new OrderSummaryProjection(), ProjectionLifecycle.Inline);
    opts.AddEntityTablesFromDbContext<OrderDbContext>();
});
```

## Conjoined Multi-Tenancy

EF Core single-stream and multi-stream projections support Marten's [conjoined multi-tenancy](/documents/multi-tenancy). When the event store uses `TenancyStyle.Conjoined`, the projection infrastructure automatically writes the tenant ID to each projected entity.

### Requirements

Your aggregate entity **must** implement `ITenanted` from `Marten.Metadata`. This interface adds a `TenantId` property that the projection infrastructure uses to write the tenant identifier:

```csharp
using Marten.Metadata;

public class TenantedOrder : ITenanted
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int ItemCount { get; set; }
    public bool IsShipped { get; set; }
    public string? TenantId { get; set; } // Required by ITenanted
}
```

The DbContext must also map the `TenantId` property to a column:

```csharp
public class TenantedOrderDbContext : DbContext
{
    public TenantedOrderDbContext(DbContextOptions<TenantedOrderDbContext> options)
        : base(options) { }

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
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
        });
    }
}
```

### Projection Class

The projection class itself does not need any special tenancy logic. The base infrastructure sets `TenantId` automatically:

```csharp
public class TenantedOrderAggregate
    : EfCoreSingleStreamProjection<TenantedOrder, Guid, TenantedOrderDbContext>
{
    public override TenantedOrder? ApplyEvent(
        TenantedOrder? snapshot, Guid identity, IEvent @event,
        TenantedOrderDbContext dbContext, IQuerySession session)
    {
        switch (@event.Data)
        {
            case OrderPlaced placed:
                return new TenantedOrder
                {
                    Id = placed.OrderId,
                    CustomerName = placed.CustomerName,
                    TotalAmount = placed.Amount,
                    ItemCount = placed.Items
                };

            case OrderShipped:
                if (snapshot != null) snapshot.IsShipped = true;
                return snapshot;
        }

        return snapshot;
    }
}
```

### Registration

```csharp
var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);
    opts.Events.TenancyStyle = TenancyStyle.Conjoined;
    opts.Add(new TenantedOrderAggregate(), ProjectionLifecycle.Inline);
});
```

### Appending Events with a Tenant

Use `ForTenant()` when opening a session to associate events with a specific tenant:

```csharp
await using var session = store.LightweightSession("tenant-alpha");
session.Events.StartStream(orderId, new OrderPlaced(orderId, "Alice", 100m, 3));
await session.SaveChangesAsync();
// The projected row in ef_tenanted_orders will have tenant_id = 'tenant-alpha'
```

### Validation

Marten validates your configuration at startup. If the event store uses conjoined tenancy but your aggregate type does not implement `ITenanted`, Marten throws an `InvalidProjectionException` with a descriptive error message.

### Limitations

- **`EfCoreEventProjection` does not support conjoined tenancy validation.** The event projection base class (`EfCoreEventProjection<TDbContext>`) is a lower-level `IProjection` implementation that does not participate in the aggregate tenancy validation. If you need multi-tenant event projections, you are responsible for reading the tenant ID from `@event.TenantId` and writing it yourself.

- **Multi-stream projections with non-unique keys across tenants.** When using `EfCoreMultiStreamProjection` with conjoined tenancy, be aware that `DbContext.FindAsync` looks up entities by primary key only, not by a composite of primary key + tenant ID. If two tenants can produce the same aggregate key (e.g., a customer name), you must ensure globally unique aggregate IDs (such as GUIDs) or configure a composite primary key in EF Core that includes the tenant ID column.

## Composite Projections

EF Core projections can participate in [composite projections](/events/projections/composite) for multi-stage processing:

```csharp
var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);
    opts.Projections.Composite(composite =>
    {
        composite.Add(opts, new OrderAggregate(), stageNumber: 1);
        composite.Add(opts, new CustomerOrderHistoryProjection(), stageNumber: 2);
    }, ProjectionLifecycle.Async);
});
```

## DbContext Configuration

All EF Core projection types expose a `ConfigureDbContext` method you can override to customize the `DbContextOptionsBuilder`. The Npgsql provider is already configured before this method is called:

```csharp
public class MyProjection
    : EfCoreSingleStreamProjection<Order, Guid, OrderDbContext>
{
    public override void ConfigureDbContext(
        DbContextOptionsBuilder<OrderDbContext> builder)
    {
        builder.EnableSensitiveDataLogging();
    }
}
```

## How It Works

Under the hood, EF Core projections:

1. **Create a per-slice DbContext** using the same PostgreSQL connection as the Marten session
2. **Register a transaction participant** so the DbContext's `SaveChangesAsync` is called within Marten's transaction, ensuring atomicity
3. **Migrate entity tables** through Weasel alongside Marten's own schema objects, so `dotnet ef` migrations are not needed
4. **Use EF Core change tracking** for insert vs. update detection (detached entities are added; unchanged entities are marked as modified)
