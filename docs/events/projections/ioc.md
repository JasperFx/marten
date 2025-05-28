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

<!-- snippet: sample_ProductProjection -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ContainerScopedProjectionTests/projections_with_IoC_services.cs#L390-L418' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_productprojection' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ContainerScopedProjectionTests/projections_with_IoC_services.cs#L69-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_projection_built_by_services' title='Start of snippet'>anchor</a></sup>
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
