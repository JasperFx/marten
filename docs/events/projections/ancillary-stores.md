# Using Ancillary Stores in Projections <Badge type="tip" text="8.x" />

## The Problem

When building systems with multiple Marten stores (using `AddMartenStore<T>()`), it's common to
need projections in one store that reference data from another. For example, a billing projection
in your primary store might need to look up tariff data from a separate `ITarievenStore`.

The natural approach — constructor injection — **does not work reliably** and can cause your
application to freeze at startup:

```csharp
// DO NOT DO THIS - can cause startup deadlock
public class InvoiceProjection(
    ITarievenStore tarievenStore
) : SingleStreamProjection<Invoice, Guid>
{
    // ...
}
```

### Why It Freezes

When you register a projection with `AddProjectionWithServices<T>()`, Marten resolves the
projection instance during store construction. If the projection's constructor depends on an
ancillary `IDocumentStore`, the DI container may attempt to resolve that store while the primary
store is still being built — creating a circular dependency that deadlocks silently.

## Solution: Inject `Lazy<T>`

Starting in Marten 8.x, `AddMartenStore<T>()` automatically registers `Lazy<T>` in the DI
container alongside the store itself. This lets you inject a lazy reference that defers
resolution until the store is actually needed — safely past the startup phase:

```csharp
public interface ITarievenStore : IDocumentStore;

public class InvoiceProjection : SingleStreamProjection<Invoice, Guid>
{
    private readonly Lazy<ITarievenStore> _tarievenStore;

    public InvoiceProjection(Lazy<ITarievenStore> tarievenStore)
    {
        _tarievenStore = tarievenStore;
    }

    public override async Task EnrichEventsAsync(
        SliceGroup<Invoice, Guid> group,
        IQuerySession querySession,
        CancellationToken cancellation)
    {
        // Safe - the store is fully constructed by the time
        // EnrichEventsAsync runs
        await using var session = _tarievenStore.Value.QuerySession();

        var ids = group.Slices
            .SelectMany(s => s.Events().OfType<IEvent<ServicePerformed>>())
            .Select(e => e.Data.TariefId)
            .Distinct().ToArray();

        var tarieven = await session.LoadManyAsync<Tarief>(cancellation, ids);

        foreach (var slice in group.Slices)
        {
            foreach (var e in slice.Events().OfType<IEvent<ServicePerformed>>())
            {
                if (tarieven.TryGetValue(e.Data.TariefId, out var tarief))
                {
                    e.Data.ResolvedPrice = tarief.Price;
                }
            }
        }
    }
}
```

Register the projection using `AddProjectionWithServices<T>()` with a **scoped** lifetime
to ensure it's resolved per-batch rather than during store construction:

```csharp
services.AddMarten(opts =>
{
    opts.Connection("primary connection string");
})
.AddProjectionWithServices<InvoiceProjection>(
    ProjectionLifecycle.Async,
    ServiceLifetime.Scoped);

services.AddMartenStore<ITarievenStore>(opts =>
{
    opts.Connection("tarieven connection string");
});
```

### Why `Lazy<T>` Works

The `Lazy<T>` wrapper is constructed immediately (it's just a thin wrapper), but the inner
`IDocumentStore` isn't resolved until `.Value` is accessed. By the time your projection's
`Apply`, `Create`, or `EnrichEventsAsync` methods execute, all stores are fully constructed
and the lazy resolution succeeds without deadlock.

### Multiple Ancillary Stores

You can inject multiple lazy store references:

```csharp
public class CrossStoreProjection : SingleStreamProjection<Summary, Guid>
{
    private readonly Lazy<ITarievenStore> _tarieven;
    private readonly Lazy<IDebtorsStore> _debtors;

    public CrossStoreProjection(
        Lazy<ITarievenStore> tarieven,
        Lazy<IDebtorsStore> debtors)
    {
        _tarieven = tarieven;
        _debtors = debtors;
    }

    // Use _tarieven.Value and _debtors.Value in your projection methods
}
```

Each `AddMartenStore<T>()` call automatically registers its own `Lazy<T>`, so no
additional configuration is needed.
