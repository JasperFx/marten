# Using Ancillary Stores in Projections

::: tip
This page documents a known limitation and the planned solution. The `IHasDependencies` interface
described here will be available in an upcoming JasperFx/Marten release.
:::

## The Problem

When building systems with multiple Marten stores (using `AddMartenStore<T>()`), it's common to
need projections in one store that reference data from another. For example, a billing projection
in your primary store might need to look up tariff data from a separate `ITarievenStore`.

The natural approach — constructor injection — **does not work reliably** and can cause your
application to freeze at startup:

```csharp
// DO NOT DO THIS - can cause startup deadlock
public class InvoiceProjection(
    ITarievenStore tarievenStore,    // ancillary store
    IDebtorsStore debtorsStore       // another ancillary store
) : SingleStreamProjection<Invoice, Guid>
{
    // ...
}
```

### Why It Freezes

When you register a projection with `AddProjectionWithServices<T>()`, Marten resolves the
projection instance during `ConfigureMarten` callbacks, which run while the primary `IDocumentStore`
singleton is being constructed. If the projection's constructor depends on an ancillary
`IDocumentStore`, the DI container may attempt to resolve that store while the primary store is
still mid-construction — creating a circular dependency that results in a deadlock with no
exception or timeout.

## Current Workaround: `Func<T>` Injection

The known workaround is to inject a factory delegate instead of the store directly:

```csharp
public class InvoiceProjection(
    Func<ITarievenStore> tarievenStore,
    Func<IDebtorsStore> debtorsStore
) : SingleStreamProjection<Invoice, Guid>
{
    public override async Task EnrichEventsAsync(
        SliceGroup<Invoice, Guid> group,
        IQuerySession querySession,
        CancellationToken cancellation)
    {
        // Resolve the store lazily - safe because by the time
        // EnrichEventsAsync runs, all stores are fully constructed
        await using var session = tarievenStore().QuerySession();
        // ... use session to look up reference data
    }
}
```

This works because `Func<T>` delegates are resolved lazily — the actual store isn't constructed
until the delegate is invoked, which happens well after startup completes.

::: warning
The `Func<T>` approach should use `ServiceLifetime.Scoped` or `ServiceLifetime.Transient` when
registering with `AddProjectionWithServices<T>()`. Using `Singleton` lifetime with `Func<T>`
still triggers resolution during store construction.
:::

## Planned Solution: `IHasDependencies`

A cleaner solution is coming via a new `IHasDependencies` interface that will be defined in the
`JasperFx` core library:

```csharp
// Coming in JasperFx 1.x / Marten 8.x
namespace JasperFx;

public interface IHasDependencies
{
    /// <summary>
    /// Called by the framework to supply the application's IServiceProvider
    /// after all stores have been fully constructed.
    /// </summary>
    void Apply(IServiceProvider services);
}
```

With this interface, projections can declare their external dependencies without constructor
injection, avoiding the circular dependency problem entirely:

```csharp
public class InvoiceProjection : SingleStreamProjection<Invoice, Guid>, IHasDependencies
{
    private ITarievenStore _tarievenStore = null!;
    private IDebtorsStore _debtorsStore = null!;

    public void Apply(IServiceProvider services)
    {
        _tarievenStore = services.GetRequiredService<ITarievenStore>();
        _debtorsStore = services.GetRequiredService<IDebtorsStore>();
    }

    public override async Task EnrichEventsAsync(
        SliceGroup<Invoice, Guid> group,
        IQuerySession querySession,
        CancellationToken cancellation)
    {
        await using var tarievenSession = _tarievenStore.QuerySession();
        
        var ids = group.Slices
            .SelectMany(s => s.Events().OfType<IEvent<ServicePerformed>>())
            .Select(e => e.Data.TariefId)
            .Distinct().ToArray();

        var tarieven = await tarievenSession.LoadManyAsync<Tarief>(cancellation, ids);
        
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

### How It Will Work

- For **singleton** projections: `Apply()` is called once during store construction, after
  the projection is resolved from the container but before it's added to the projection graph.
- For **scoped** projections: `Apply()` is called at the start of each processing batch,
  after the scoped instance is resolved from the container.
- The `IServiceProvider` passed to `Apply()` is the full application-level provider (or the
  scoped provider for scoped projections), so all registered services including ancillary
  stores are available.

### Cross-Store Enrichment

The `IHasDependencies` pattern is particularly useful for the event enrichment scenario
described in [Enriching Events](/events/projections/enrichment). Since `EnrichEventsAsync`
only receives a query session from the *owning* store, accessing reference data from an
ancillary store currently requires the `Func<T>` workaround or (once available) the
`IHasDependencies` pattern shown above.

A future enhancement may add a declarative `.FromStore<TStore>()` step to the fluent
enrichment API:

```csharp
// Future API - not yet available
await group
    .EnrichWith<Tarief>()
    .FromStore<ITarievenStore>()
    .ForEvent<ServicePerformed>()
    .ForEntityId(x => x.TariefId)
    .EnrichAsync((slice, e, tarief) =>
    {
        e.Data.ResolvedPrice = tarief.Price;
    });
```

## Summary

| Approach | Status | Lifetime Support | Notes |
|----------|--------|-----------------|-------|
| Constructor injection | Broken (deadlock) | N/A | Do not use for ancillary stores |
| `Func<T>` injection | Works today | Scoped/Transient only | Workaround; lazy resolution avoids deadlock |
| `IHasDependencies` | Planned | All lifetimes | Clean, framework-supported pattern |
| Fluent `.FromStore<T>()` | Future | All lifetimes | Declarative enrichment from other stores |
