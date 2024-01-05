# Bootstrapping in .Net Applications

:::tip
The exact formula for bootstrapping .Net applications has changed quite a bit from early .Net Core to the latest `WebApplication` model in .Net 6.0 at the time this page was last updated. Regardless, the `IServiceCollection` abstraction for registering services in an IoC container has remained stable and everything in this page functions against that model.
:::

As briefly shown in the [getting started](/) page, Marten comes with extension methods for the .Net Core standard `IServiceCollection` to quickly add Marten services to any .Net application that is bootstrapped by either the [Generic IHostBuilder abstraction](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host) or the [ASP.Net Core IWebHostBuilder](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.hosting.iwebhostbuilder) or the [.Net 6 WebApplication](https://docs.microsoft.com/en-us/aspnet/core/migration/50-to-60?view=aspnetcore-6.0&tabs=visual-studio#new-hosting-model) hosting models.

Jumping right into a basic ASP&#46;NET Core application using the out of the box Web API template, you'd have a class called `Startup` that holds most of the configuration for your application including
the IoC service registrations for your application in the `Startup.ConfigureServices()` method. To add Marten to your application, use the `AddMarten()` method as shown below:

<!-- snippet: sample_StartupConfigureServices -->
<a id='snippet-sample_startupconfigureservices'></a>
```cs
// This is the absolute, simplest way to integrate Marten into your
// .NET application with Marten's default configuration
builder.Services.AddMarten(options =>
{
    // Establish the connection string to your Marten database
    options.Connection(builder.Configuration.GetConnectionString("Marten")!);

    // If we're running in development mode, let Marten just take care
    // of all necessary schema building and patching behind the scenes
    if (builder.Environment.IsDevelopment())
    {
        options.AutoCreateSchemaObjects = AutoCreate.All;
    }
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Program.cs#L14-L29' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_startupconfigureservices' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `AddMarten()` method will add these service registrations to your application:

1. `IDocumentStore` with a *Singleton* lifetime. The document store can be used to create sessions, query the configuration of Marten, generate schema migrations, and do bulk inserts.
2. `IDocumentSession` with a *Scoped* lifetime for all read and write operations. **By default**, this is done with the `IDocumentStore.OpenSession()` method and the session created will have the identity map behavior
3. `IQuerySession` with a *Scoped* lifetime for all read operations against the document store.

For more information, see:

* [Dependency injection in ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection) for more explanation about the service lifetime behavior.
* Check [identity map mechanics](/documents/identity) for an explanation of Marten session behavior
* Check [storing documents and unit of work](/documents/sessions) for session basics

At runtime, when your application needs to resolve `IDocumentStore` for the first time, Marten will:

1. Resolve a `StoreOptions` object from the initial `AddMarten()` configuration
2. Apply all registered `IConfigureMarten` services to alter that `StoreOptions` object
3. Reads the `IHostEnvironment` for the application if it exists to try to determine the main application assembly and paths for generated code output
4. Attaches any `IInitialData` services that were registered in the IoC container to the `StoreOptions` object
5. *Finally*, Marten builds a new `DocumentStore` object using the now configured `StoreOptions` object

This model is comparable to the .Net `IOptions` model.

## Register DocumentStore with AddMarten()

::: tip I
