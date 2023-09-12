## Projections and IoC Services

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

snippet: sample_ProductProjection

Now, we *want* to use this projection at runtime within Marten, and need to register the projection
type while also letting our application's IoC container deal with its dependencies. That can be
done with the `AddProjectionWithServices<T>()` method shown below:

snippet: sample_registering_projection_built_by_services

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

