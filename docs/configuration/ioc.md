# Custom IoC Integration

::: tip
The Marten team recommends using the `IServiceCollection.AddMarten()` extension method
for IoC integration out of the box.
:::

The Marten team has striven to make the library perfectly usable without the usage of an IoC container, but you may still want to
use an IoC container specifically to manage dependencies and the life cycle of Marten objects.
While the `IServiceCollection.AddMarten()` method is the recommended way to integrate Marten
into an IoC container, you can certainly recreate that functionality in the IoC container
of your choice.

::: tip INFO
Lamar supports the .Net Core abstractions for IoC service registrations, so you *could* happily
use the `AddMarten()` method directly with Lamar as well.
:::

Using [Lamar](https://jasperfx.github.io/lamar) as the example container, we recommend registering Marten something like this:

<!-- snippet: sample_MartenServices -->
<a id='snippet-sample_martenservices'></a>
```cs
public class MartenServices : ServiceRegistry
{
    public MartenServices()
    {
        ForSingletonOf<IDocumentStore>().Use(c =>
        {
            return DocumentStore.For(options =>
            {
                options.Connection("your connection string");
                options.AutoCreateSchemaObjects = AutoCreate.None;

                // other Marten configuration options
            });
        });

        // Register IDocumentSession as Scoped
        For<IDocumentSession>()
            .Use(c => c.GetInstance<IDocumentStore>().LightweightSession())
            .Scoped();

        // Register IQuerySession as Scoped
        For<IQuerySession>()
            .Use(c => c.GetInstance<IDocumentStore>().QuerySession())
            .Scoped();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/DevelopmentModeRegistry.cs#L8-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_martenservices' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

There are really only two key points here:

1. There should only be one `IDocumentStore` object instance created in your application, so I scoped it as a "Singleton" in the StructureMap container
1. The `IDocumentSession` service that you use to read and write documents should be scoped as "one per transaction." In typical usage, this
   ends up meaning that an `IDocumentSession` should be scoped to a single HTTP request in web applications or a single message being handled in service
   bus applications.
