# Projections and IoC Services

::: warning
This functionality will only work with projection types that directly implement that `IProjection`
interface. So no usage of `EventProjection` or the `Single/MultiStreamProjection` base classes. Aggregation
can still be done with `CustomAggregation<T, TId>` as your base class though.
:::

Many Marten users have had some reason to use services from the current application's Inversion of Control (IoC) container
within their projections. While that's always been technically possible, Marten has not had explicit support for this
until this feature introduced in 6.2.

Let's say you have a custom aggregation projection like this one below that needs to use a service named
`IPriceLookup` at runtime:

<!-- snippet: sample_productprojection -->
<a id='snippet-sample_productprojection'></a>
```cs
public class ProductProjection: SingleStreamProjection<Product, Guid>
{
    private readonly IPriceLookup _lookup;

    // The lookup service would be injected by IoC
    public ProductProjection(IPriceLookup lookup)
    {
        _lookup = lookup;
        Name = "Product";
    }

    public override Product Evolve(Product snapshot, Guid id, IEvent e)
    {
        snapshot ??= new Product { Id = id };

        if (e.Data is ProductRegistered r)
        {
            snapshot.Price = _lookup.PriceFor(r.Category);
            snapshot.Name = r.Name;
            snapshot.Category = r.Category;
        }

        return snapshot;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ContainerScopedProjectionTests/projections_with_IoC_services.cs#L509-L537' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_productprojection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Now, we *want* to use this projection at runtime within Marten, and need to register the projection
type while also letting our application's IoC container deal with its dependencies. That can be
done with the `AddProjectionWithServices<T>()` method shown below:

<!-- snippet: sample_registering_projection_built_by_services -->
<a id='snippet-sample_registering_projection_built_by_services'></a>
```cs
using var host = await Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddSingleton<IPriceLookup, PriceLookup>();

        services.AddMarten(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = "ioc5";
                opts.ApplyChangesLockId = opts.ApplyChangesLockId + 2;
            })
            // Note that this is chained after the call to AddMarten()
            .AddProjectionWithServices<ProductProjection>(
                ProjectionLifecycle.Inline,
                ServiceLifetime.Singleton
            );
    })
    .StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ContainerScopedProjectionTests/projections_with_IoC_services.cs#L78-L99' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_projection_built_by_services' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note that we're having to explicitly specify the projection lifecycle for the projection used within
Marten (Inline vs Async vs Live), and also the `ServiceLifetime` that governs the projection object's
lifetime at runtime.

::: warning
Marten is *not* using the shared scoped container from ASP.Net Core requests or service bus
requests when this runs for inline projections
:::

If the registration is a `Singleton`, Marten will use the application's IoC container to build the
projection object once and add it to Marten's configuration at application start up time. If the
registration is `Scoped` or `Transient`, Marten uses a proxy wrapper around `IProjection` that builds
the projection object uniquely for each usage through scoped containers, and disposes the inner projection
object at the end of the operation.

## Registering DI-aware projections through IConfigureMarten modules

If you organize your application's Marten configuration into [modules backed by `IConfigureMarten`](/configuration/hostbuilder#composite-configuration-with-configuremarten)
and one of those modules needs to register a projection (or subscription) that depends on IoC
services, the answer is *not* a special "module-aware" overload of `AddProjectionWithServices` —
it's the constructor of your `IConfigureMarten` itself. The IoC container resolves the
`IConfigureMarten` implementation, so any service it needs can be declared as a constructor
dependency, and then handed to a projection that you build explicitly inside `Configure`.

Using the same `ProductProjection` from above (which needs an `IPriceLookup`):

<!-- snippet: sample_iconfiguremarten_with_di_projection -->
<a id='snippet-sample_iconfiguremarten_with_di_projection'></a>
```cs
// An IConfigureMarten that takes its own dependencies through the constructor and
// uses them to build a service-aware projection. This is the standard path for
// modular Marten configuration that needs DI services to register a projection or
// subscription -- the IoC container resolves IPriceLookup when it constructs the
// IConfigureMarten implementation, and the resolved instance is then passed to the
// projection at registration time.
internal class ProductProjectionRegistration: IConfigureMarten
{
    private readonly IPriceLookup _lookup;

    public ProductProjectionRegistration(IPriceLookup lookup)
    {
        _lookup = lookup;
    }

    public void Configure(IServiceProvider services, StoreOptions options)
    {
        // Build the projection instance with the injected dependencies and
        // register it on StoreOptions like any other projection
        options.Projections.Add(new ProductProjection(_lookup), ProjectionLifecycle.Inline);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ContainerScopedProjectionTests/projections_with_IoC_services.cs#L570-L595' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_iconfiguremarten_with_di_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The module's `IServiceCollection` extension wires both the dependency *and* the `IConfigureMarten`
into DI. The host's `AddMarten(...)` call lives elsewhere — this module just contributes to it:

<!-- snippet: sample_addproductmodule_with_iconfiguremarten -->
<a id='snippet-sample_addproductmodule_with_iconfiguremarten'></a>
```cs
public static class ProductModuleExtensions
{
    /// <summary>
    /// Module-style registration: the module owns the IPriceLookup service AND the
    /// IConfigureMarten that uses it to register a service-aware projection. The
    /// host's AddMarten(...) call lives elsewhere; this module just adds to it.
    /// </summary>
    public static IServiceCollection AddProductModule(this IServiceCollection services)
    {
        services.AddSingleton<IPriceLookup, PriceLookup>();

        // The IConfigureMarten implementation will receive IPriceLookup through
        // its constructor at IoC resolution time, then build the projection.
        services.AddSingleton<IConfigureMarten, ProductProjectionRegistration>();

        return services;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ContainerScopedProjectionTests/projections_with_IoC_services.cs#L597-L618' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addproductmodule_with_iconfiguremarten' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The host then composes the modules:

```cs
services.AddMarten(opts =>
{
    opts.Connection(connectionString);
}).ApplyAllDatabaseChangesOnStartup();

services.AddProductModule();
```

::: tip
This pattern works equally well for **subscriptions** — register an `IConfigureMarten` that takes the
subscription's dependencies through its constructor, build the subscription instance inside
`Configure`, and call `options.Events.Subscribe(...)`. The same constructor-injection trick lets a
module wire any DI-resolved object into Marten's configuration without going through
`AddProjectionWithServices`.
:::

The same shape applies if you have multiple `IDocumentStore` instances and need module-style
registration against a specific store — implement `IConfigureMarten<TStore>` instead of
`IConfigureMarten` and register it the same way (`services.AddSingleton<IConfigureMarten<IInvoicingStore>, …>()`).

### When to use which

| Need | Use |
| --- | --- |
| Compose Marten configuration from multiple modules, each contributing a service-aware projection | `IConfigureMarten` with constructor injection (above) |
| Register a single service-aware projection inline against the main `AddMarten()` call | [`AddProjectionWithServices<T>`](#projections-and-ioc-services) |
| The projection's dependency itself has a `Scoped` lifetime that must be created per use | `AddProjectionWithServices<T>(..., ServiceLifetime.Scoped)` so Marten builds a fresh projection (and dependency) per invocation |
