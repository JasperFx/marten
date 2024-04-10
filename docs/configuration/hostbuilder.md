# Bootstrapping Marten

As briefly shown in the [getting started](/) page, Marten comes with the `AddMarten()` extension method for the .NET `IServiceCollection` to quickly add Marten to any ASP&#46;NET Core or Worker Service application:

<!-- snippet: sample_StartupConfigureServices -->
<a id='snippet-sample_startupconfigureservices'></a>
```cs
// This is the absolute, simplest way to integrate Marten into your
// .NET application with Marten's default configuration
builder.Services.AddMarten(options =>
{
    // Establish the connection string to your Marten database
    options.Connection(builder.Configuration.GetConnectionString("Marten")!);

    // Specify that we want to use STJ as our serializer
    options.UseSystemTextJsonForSerialization();

    // If we're running in development mode, let Marten just take care
    // of all necessary schema building and patching behind the scenes
    if (builder.Environment.IsDevelopment())
    {
        options.AutoCreateSchemaObjects = AutoCreate.All;
    }
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Program.cs#L15-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_startupconfigureservices' title='Start of snippet'>anchor</a></sup>
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
3. Apply all registered `IAsyncConfigureMarten` services to alter that `StoreOptions` object
4. Reads the `IHostEnvironment` for the application if it exists to try to determine the main application assembly and paths for generated code output
5. Attaches any `IInitialData` services that were registered in the IoC container to the `StoreOptions` object
6. *Finally*, Marten builds a new `DocumentStore` object using the now configured `StoreOptions` object

This model is comparable to the .Net `IOptions` model.

## Register DocumentStore with AddMarten()

::: tip INFO
All the examples in this page are assuming the usage of the default IoC container `Microsoft.Extensions.DependencyInjection`, but Marten can be used with any IoC container or with no IoC container whatsoever.
:::

First, if you are using Marten completely out of the box with no customizations (besides attributes on your documents), you can just supply a connection string to the underlying Postgresql database like this:

<!-- snippet: sample_AddMartenByConnectionString -->
<a id='snippet-sample_addmartenbyconnectionstring'></a>
```cs
var connectionString = Configuration.GetConnectionString("postgres");

// By only the connection string
services.AddMarten(connectionString);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/ByConnectionString/Startup.cs#L18-L26' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenbyconnectionstring' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The second option is to supply a [nested closure](https://martinfowler.com/dslCatalog/nestedClosure.html) to configure Marten inline like so:

<!-- snippet: sample_AddMartenByNestedClosure -->
<a id='snippet-sample_addmartenbynestedclosure'></a>
```cs
var connectionString = Configuration.GetConnectionString("postgres");

services.AddMarten(opts =>
    {
        opts.Connection(connectionString);
    })
    // Using the "Optimized artifact workflow" for Marten >= V5
    // sets up your Marten configuration based on your environment
    // See https://martendb.io/configuration/optimized_artifact_workflow.html
    .OptimizeArtifactWorkflow();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/ByNestedClosure/Startup.cs#L24-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenbynestedclosure' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Lastly, if you prefer, you can pass a Marten `StoreOptions` object to `AddMarten()` like this example:

<!-- snippet: sample_AddMartenByStoreOptions -->
<a id='snippet-sample_addmartenbystoreoptions'></a>
```cs
var connectionString = Configuration.GetConnectionString("postgres");

// Build a StoreOptions object yourself
var options = new StoreOptions();
options.Connection(connectionString);

services.AddMarten(options)
    // Using the "Optimized artifact workflow" for Marten >= V5
    // sets up your Marten configuration based on your environment
    // See https://martendb.io/configuration/optimized_artifact_workflow.html
    .OptimizeArtifactWorkflow();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/ByStoreOptions/Startup.cs#L23-L37' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenbystoreoptions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Using NpgsqlDataSource <Badge type="tip" text="7.0" />

::: tip
You will have to use the `NpgsqlDataSource` registration if you want to opt into Npgsql logging. See the [Npgsql documentation on logging](https://www.npgsql.org/doc/diagnostics/logging.html?tabs=console)
for more information.
:::

You can also use the [NpgsqlDataSource](https://www.npgsql.org/doc/basic-usage.html#data-source) to configure Marten connection settings. From [Npgsql docs](https://www.npgsql.org/doc/basic-usage.html#data-source):

> The data source represents your PostgreSQL database and can hand out connections to it or support direct execution of SQL against it. The data source encapsulates the various Npgsql configuration needed to connect to PostgreSQL, as well the connection pooling which makes Npgsql efficient.

You can use the `AddNpgsqlDataSource` method from [Npgsql.DependencyInjection package](https://www.nuget.org/packages/Npgsql.DependencyInjection) to perform a setup by calling the `UseNpgsqlDataSourceMethod`:

<!-- snippet: sample_using_UseNpgsqlDataSource -->
<a id='snippet-sample_using_usenpgsqldatasource'></a>
```cs
services.AddNpgsqlDataSource(ConnectionSource.ConnectionString);

services.AddMarten()
    .UseLightweightSessions()
    .UseNpgsqlDataSource();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/MartenServiceCollectionExtensionsTests.cs#L291-L299' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_usenpgsqldatasource' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you're on .NET 8 (and above), you can also use a dedicated [keyed registration](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8#keyed-di-services). This can be useful for scenarios where you need more than one data source registered:

<!-- snippet: sample_using_UseNpgsqlDataSource -->
<a id='snippet-sample_using_usenpgsqldatasource'></a>
```cs
services.AddNpgsqlDataSource(ConnectionSource.ConnectionString);

services.AddMarten()
    .UseLightweightSessions()
    .UseNpgsqlDataSource();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/MartenServiceCollectionExtensionsTests.cs#L291-L299' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_usenpgsqldatasource' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Composite Configuration with ConfigureMarten()

The `AddMarten()` mechanism assumes that you are expressing all of the Marten configuration in one place and "know" what that configuration is upfront. Consider these possibilities where that isn't necessarily possible or desirable:

1. You want to override Marten configuration in integration testing scenarios (I do this quite commonly)
2. Many users have expressed the desire to keep parts of Marten configuration in potentially separate assemblies or subsystems in such a way that they could later break up the current service into smaller services

Fear not, Marten V5.0 introduced a new way to add or modify the Marten configuration from `AddMarten()`. Let's assume that we're building a system that has a subsystem related to *users* and want to segregate all the service registrations and Marten configuration related to *users* into a single place like this extension method:

<!-- snippet: sample_AddUserModule -->
<a id='snippet-sample_addusermodule'></a>
```cs
public static IServiceCollection AddUserModule(this IServiceCollection services)
{
    // This applies additional configuration to the main Marten DocumentStore
    // that is configured elsewhere
    services.ConfigureMarten(opts =>
    {
        opts.RegisterDocumentType<User>();
    });

    // Other service registrations specific to the User submodule
    // within the bigger system

    return services;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/BootstrappingExamples.cs#L14-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addusermodule' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And next, let's put that into context with its usage inside your application's bootstrapping:

<!-- snippet: sample_using_configure_marten -->
<a id='snippet-sample_using_configure_marten'></a>
```cs
using var host = await Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        // The initial Marten configuration
        services.AddMarten("some connection string");

        // Other core service registrations
        services.AddLogging();

        // Add the User module
        services.AddUserModule();
    }).StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/BootstrappingExamples.cs#L83-L98' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_configure_marten' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `ConfigureMarten()` method is the interesting part of the code samples above. That is registering a small
service that implements the `IConfigureMarten` interface into the underlying IoC container:

<!-- snippet: sample_IConfigureMarten -->
<a id='snippet-sample_iconfiguremarten'></a>
```cs
/// <summary>
///     Mechanism to register additional Marten configuration that is applied after AddMarten()
///     configuration, but before DocumentStore is initialized
/// </summary>
public interface IConfigureMarten
{
    void Configure(IServiceProvider services, StoreOptions options);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/MartenServiceCollectionExtensions.cs#L894-L905' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_iconfiguremarten' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You could alternatively implement a custom `IConfigureMarten` (or `IConfigureMarten<T> where T : IDocumentStore` if you're [working with multiple databases](#working-with-multiple-marten-databases)) class like so:

<!-- snippet: sample_UserMartenConfiguration -->
<a id='snippet-sample_usermartenconfiguration'></a>
```cs
internal class UserMartenConfiguration: IConfigureMarten
{
    public void Configure(IServiceProvider services, StoreOptions options)
    {
        options.RegisterDocumentType<User>();
        // and any other additional Marten configuration
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/BootstrappingExamples.cs#L64-L75' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_usermartenconfiguration' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

and registering it in your IoC container something like this:

<!-- snippet: sample_AddUserModule2 -->
<a id='snippet-sample_addusermodule2'></a>
```cs
public static IServiceCollection AddUserModule2(this IServiceCollection services)
{
    // This applies additional configuration to the main Marten DocumentStore
    // that is configured elsewhere
    services.AddSingleton<IConfigureMarten, UserMartenConfiguration>();

    // If you're using multiple databases per Host, register `IConfigureMarten<T>`, like this:
    services.AddSingleton<IConfigureMarten<IInvoicingStore>, InvoicingStoreConfiguration>();

    // Other service registrations specific to the User submodule
    // within the bigger system

    return services;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/BootstrappingExamples.cs#L36-L53' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addusermodule2' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Using IoC Services for Configuring Marten <Badge type="tip" text="7.7" />

There is also a newer mechanism called `IAsyncConfigureMarten` that was originally built to enable services
like the [Feature Management library from Microsoft](https://learn.microsoft.com/en-us/azure/azure-app-configuration/use-feature-flags-dotnet-core) to
be used to selectively configure Marten using potentially asynchronous methods and IoC resolved services. 

That interface signature is:

<!-- snippet: sample_IAsyncConfigureMarten -->
<a id='snippet-sample_iasyncconfiguremarten'></a>
```cs
/// <summary>
///     Mechanism to register additional Marten configuration that is applied after AddMarten()
///     configuration, but before DocumentStore is initialized when you need to utilize some
/// kind of asynchronous services like Microsoft's FeatureManagement feature to configure Marten
/// </summary>
public interface IAsyncConfigureMarten
{
    ValueTask Configure(StoreOptions options, CancellationToken cancellationToken);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/MartenServiceCollectionExtensions.cs#L907-L919' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_iasyncconfiguremarten' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As an example from the tests, here's a custom version that uses the Feature Management service:

<!-- snippet: sample_FeatureManagementUsingExtension -->
<a id='snippet-sample_featuremanagementusingextension'></a>
```cs
public class FeatureManagementUsingExtension: IAsyncConfigureMarten
{
    private readonly IFeatureManager _manager;

    public FeatureManagementUsingExtension(IFeatureManager manager)
    {
        _manager = manager;
    }

    public async ValueTask Configure(StoreOptions options, CancellationToken cancellationToken)
    {
        if (await _manager.IsEnabledAsync("Module1"))
        {
            options.Events.MapEventType<Module1Event>("module1:event");
        }
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/configuring_marten_with_async_extensions.cs#L77-L97' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_featuremanagementusingextension' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And lastly, these extensions can be registered directly against `IServiceCollection` like so:

<!-- snippet: sample_registering_async_config_marten -->
<a id='snippet-sample_registering_async_config_marten'></a>
```cs
services.ConfigureMartenWithServices<FeatureManagementUsingExtension>();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/configuring_marten_with_async_extensions.cs#L34-L38' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_async_config_marten' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Using Lightweight Sessions 

::: tip
Most usages of Marten should default to the lightweight sessions for better performance
:::

The default registration for `IDocumentSession` added by `AddMarten()` is a session with
[identity map](/documents/sessions.html#identity-map-mechanics) mechanics. That might be unnecessary
overhead in most cases where the sessions are short-lived, but we keep this behavior for backward
compatibility with early Marten and RavenDb behavior before that. To opt into using lightweight sessions
without the identity map behavior, use this syntax:

<!-- snippet: sample_AddMartenWithLightweightSessions -->
<a id='snippet-sample_addmartenwithlightweightsessions'></a>
```cs
var connectionString = Configuration.GetConnectionString("postgres");

services.AddMarten(opts =>
    {
        opts.Connection(connectionString);
    })

    // Chained helper to replace the built in
    // session factory behavior
    .UseLightweightSessions();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/LightweightSessions/Startup.cs#L23-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenwithlightweightsessions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Customizing Session Creation Globally

By default, Marten will create a document session with the basic identity map enabled and a [ReadCommitted](https://docs.microsoft.com/en-us/dotnet/api/system.transactions.isolationlevel?view=netcore-3.1) transaction isolation level. If you want to use a different configuration for sessions globally in your application, you can use a custom implementation of the `ISessionFactory` class
as shown in this example:

<!-- snippet: sample_CustomSessionFactory -->
<a id='snippet-sample_customsessionfactory'></a>
```cs
public class CustomSessionFactory: ISessionFactory
{
    private readonly IDocumentStore _store;

    // This is important! You will need to use the
    // IDocumentStore to open sessions
    public CustomSessionFactory(IDocumentStore store)
    {
        _store = store;
    }

    public IQuerySession QuerySession()
    {
        return _store.QuerySession();
    }

    public IDocumentSession OpenSession()
    {
        // Opting for the "lightweight" session
        // option with no identity map tracking
        // and choosing to use Serializable transactions
        // just to be different
        return _store.LightweightSession(IsolationLevel.Serializable);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/ConfiguringSessionCreation/Startup.cs#L11-L39' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customsessionfactory' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To register the custom session factory, use the `BuildSessionsWith()` method as shown in this example:

<!-- snippet: sample_AddMartenWithCustomSessionCreation -->
<a id='snippet-sample_addmartenwithcustomsessioncreation'></a>
```cs
var connectionString = Configuration.GetConnectionString("postgres");

services.AddMarten(opts =>
    {
        opts.Connection(connectionString);
    })
    // Using the "Optimized artifact workflow" for Marten >= V5
    // sets up your Marten configuration based on your environment
    // See https://martendb.io/configuration/optimized_artifact_workflow.html
    .OptimizeArtifactWorkflow()
    // Chained helper to replace the built in
    // session factory behavior
    .BuildSessionsWith<CustomSessionFactory>();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/ConfiguringSessionCreation/Startup.cs#L54-L68' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenwithcustomsessioncreation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The session factories can also be used to build out and attach custom `IDocumentSessionListener` objects or replace the logging as we'll see in the next section.

See [diagnostics and instrumentation](/diagnostics) for more information.

## Customizing Session Creation by Scope

From a recent user request to Marten, what if you want to log the database statement activity in Marten with some kind of correlation to the active HTTP request or service bus message or some other logical
session identification in your application? That's now possible by using a custom `ISessionFactory`.

Taking the example of an ASP&#46;NET Core application, let's say that you have a small service scoped to an HTTP request that tracks a correlation identifier for the request like this:

<!-- snippet: sample_CorrelationIdWithISession -->
<a id='snippet-sample_correlationidwithisession'></a>
```cs
public interface ISession
{
    Guid CorrelationId { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/PerScopeSessionCreation/Startup.cs#L15-L20' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_correlationidwithisession' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And a custom Marten session logger to add the correlation identifier to the log output like this:

<!-- snippet: sample_CorrelatedMartenLogger -->
<a id='snippet-sample_correlatedmartenlogger'></a>
```cs
public class CorrelatedMartenLogger: IMartenSessionLogger
{
    private readonly ILogger<IDocumentSession> _logger;
    private readonly ISession _session;

    public CorrelatedMartenLogger(ILogger<IDocumentSession> logger, ISession session)
    {
        _logger = logger;
        _session = session;
    }

    public void LogSuccess(NpgsqlCommand command)
    {
        // Do some kind of logging using the correlation id of the ISession
    }

    public void LogFailure(NpgsqlCommand command, Exception ex)
    {
        // Do some kind of logging using the correlation id of the ISession
    }

    public void LogSuccess(NpgsqlBatch batch)
    {
        // Do some kind of logging using the correlation id of the ISession
    }

    public void LogFailure(NpgsqlBatch batch, Exception ex)
    {
        // Do some kind of logging using the correlation id of the ISession
    }

    public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
    {
        // Do some kind of logging using the correlation id of the ISession
    }

    public void OnBeforeExecute(NpgsqlCommand command)
    {

    }

    public void LogFailure(Exception ex, string message)
    {

    }

    public void OnBeforeExecute(NpgsqlBatch batch)
    {

    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/PerScopeSessionCreation/Startup.cs#L22-L74' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_correlatedmartenlogger' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Now, let's move on to building out a custom session factory that will attach our correlated marten logger to sessions being resolved from the IoC container:

<!-- snippet: sample_CustomSessionFactoryByScope -->
<a id='snippet-sample_customsessionfactorybyscope'></a>
```cs
public class ScopedSessionFactory: ISessionFactory
{
    private readonly IDocumentStore _store;
    private readonly ILogger<IDocumentSession> _logger;
    private readonly ISession _session;

    // This is important! You will need to use the
    // IDocumentStore to open sessions
    public ScopedSessionFactory(IDocumentStore store, ILogger<IDocumentSession> logger, ISession session)
    {
        _store = store;
        _logger = logger;
        _session = session;
    }

    public IQuerySession QuerySession()
    {
        return _store.QuerySession();
    }

    public IDocumentSession OpenSession()
    {
        var session = _store.LightweightSession();

        // Replace the Marten session logger with our new
        // correlated marten logger
        session.Logger = new CorrelatedMartenLogger(_logger, _session);

        return session;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/PerScopeSessionCreation/Startup.cs#L77-L109' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customsessionfactorybyscope' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Lastly, let's register our new session factory, but this time we need to take care to register the session factory as `Scoped` in the underlying container so we're using the correct `ISession` at runtime:

<!-- snippet: sample_AddMartenWithCustomSessionCreationByScope -->
<a id='snippet-sample_addmartenwithcustomsessioncreationbyscope'></a>
```cs
var connectionString = Configuration.GetConnectionString("postgres");

services.AddMarten(opts =>
    {
        opts.Connection(connectionString);
    })
    // Using the "Optimized artifact workflow" for Marten >= V5
    // sets up your Marten configuration based on your environment
    // See https://martendb.io/configuration/optimized_artifact_workflow.html
    .OptimizeArtifactWorkflow()
    // Chained helper to replace the CustomSessionFactory
    .BuildSessionsWith<ScopedSessionFactory>(ServiceLifetime.Scoped);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/PerScopeSessionCreation/Startup.cs#L125-L138' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenwithcustomsessioncreationbyscope' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
This correlation tracking might be better with structural logging with something like [Serilog](https://serilog.net), but we'll leave that to users.
:::

## Eager Initialization of the DocumentStore

Lastly, if desirable, you can force Marten to initialize the applications document store as part of bootstrapping instead of waiting for it to be initialized on the first usage with this syntax:

<!-- snippet: sample_AddMartenWithEagerInitialization -->
<a id='snippet-sample_addmartenwitheagerinitialization'></a>
```cs
var connectionString = Configuration.GetConnectionString("postgres");

// By only the connection string
services.AddMarten(connectionString)
    // Using the "Optimized artifact workflow" for Marten >= V5
    // sets up your Marten configuration based on your environment
    // See https://martendb.io/configuration/optimized_artifact_workflow.html
    .OptimizeArtifactWorkflow()
    // Spin up the DocumentStore right this second!
    .InitializeWith();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/EagerInitialization/Startup.cs#L19-L32' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenwitheagerinitialization' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Working with Multiple Marten Databases

:::tip
This feature is **not** meant for multi-tenancy with separate databases. This is specifically meant for use
cases where a single system needs to work with two or more semantically different Marten databases.
:::

:::tip
The database management tools in Marten.CommandLine are able to work with the separately registered
document stores along with the default store from `AddMarten()`.
:::

Marten V5.0 introduces a new feature to register additional Marten databases into a .Net system. `AddMarten()` continues to work as it has, but we can now register and resolve additional store services. To utilize the type system and your application's underlying IoC container, the first step is to create a custom *marker* interface for your separate document store like this one below targeting a separate "invoicing" database:

<!-- snippet: sample_IInvoicingStore -->
<a id='snippet-sample_iinvoicingstore'></a>
```cs
// These marker interfaces *must* be public
public interface IInvoicingStore : IDocumentStore
{

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/MultipleDocumentStores.cs#L57-L65' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_iinvoicingstore' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A couple notes on the interface:

1. The custom interface has to be public and implement the `IDocumentStore` interface
2. Marten is quietly building a dynamic type for your additional store interface internally

And now to bootstrap that separate store in our system:

<!-- snippet: sample_bootstrapping_separate_Store -->
<a id='snippet-sample_bootstrapping_separate_store'></a>
```cs
using var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        // You can still use AddMarten() for the main document store
        // of this application
        services.AddMarten("some connection string");

        services.AddMartenStore<IInvoicingStore>(opts =>
            {
                // All the normal options are available here
                opts.Connection("different connection string");

                // more configuration
            })
            // Optionally apply all database schema
            // changes on startup
            .ApplyAllDatabaseChangesOnStartup()

            // Run the async daemon for this database
            .AddAsyncDaemon(DaemonMode.HotCold)

            // Use IInitialData
            .InitializeWith(new DefaultDataSet())

            // Use the V5 optimized artifact workflow
            // with the separate store as well
            .OptimizeArtifactWorkflow();
    }).StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/MultipleDocumentStores.cs#L14-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bootstrapping_separate_store' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

At runtime we can inject an instance of our new `IInvoicingStore` and work with it like any other
Marten `IDocumentStore` as shown below in an internal `InvoicingService`:

<!-- snippet: sample_InvoicingService -->
<a id='snippet-sample_invoicingservice'></a>
```cs
public class InvoicingService
{
    private readonly IInvoicingStore _store;

    // IInvoicingStore can be injected like any other
    // service in your IoC container
    public InvoicingService(IInvoicingStore store)
    {
        _store = store;
    }

    public async Task DoSomethingWithInvoices()
    {
        // Important to dispose the session when you're done
        // with it
        await using var session = _store.LightweightSession();

        // do stuff with the session you just opened
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/MultipleDocumentStores.cs#L67-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_invoicingservice' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
