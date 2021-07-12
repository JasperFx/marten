# Integrating Marten into .Net Core Applications

::: tip INFO
The built in DI service registration helpers were introduced in Marten v3.12.
:::

If your application uses an [IoC container](https://en.wikipedia.org/wiki/Inversion_of_control),
the easiest way to integrate Marten into a .Net application is to add the key Marten services to
the underlying IoC container for the application.

As briefly shown in the [getting started](/guide/) page, Marten comes with extension methods
for the .Net Core standard `IServiceCollection` to quickly add Marten services to any .Net Core application that is bootstrapped by
either the [Generic IHostBuilder abstraction](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-3.1) or the slightly older [ASP.Net Core IWebHostBuilder](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.hosting.iwebhostbuilder?view=aspnetcore-3.1)
hosting models.

Jumping right into a basic ASP.Net MVC Core application using the out of the box Web API template, you'd have a class called `Startup` that holds most of the configuration for your application including
the IoC service registrations for your application in the `ConfigureServices()` method. To add Marten
to your application, use the `AddMarten()` method as shown below:

<!-- snippet: sample_StartupConfigureServices -->
<a id='snippet-sample_startupconfigureservices'></a>
```cs
public class Startup
{
    public IConfiguration Configuration { get; }
    public IHostEnvironment Environment { get; }

    public Startup(IConfiguration configuration, IHostEnvironment environment)
    {
        Configuration = configuration;
        Environment = environment;
    }

    // This method gets called by the runtime. Use this method to add services to the container.
    // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
    public void ConfigureServices(IServiceCollection services)
    {
        // This is the absolute, simplest way to integrate Marten into your
        // .Net Core application with Marten's default configuration
        services.AddMarten(options =>
        {
            // Establish the connection string to your Marten database
            options.Connection(Configuration.GetConnectionString("Marten"));

            // If we're running in development mode, let Marten just take care
            // of all necessary schema building and patching behind the scenes
            if (Environment.IsDevelopment())
            {
                options.AutoCreateSchemaObjects = AutoCreate.All;
            }
        });
    }

    // and other methods we don't care about right now...
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Startup.cs#L12-L47' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_startupconfigureservices' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `AddMarten()` method will add these service registrations to your application:

1. `IDocumentStore` with a *Singleton* lifetime. The document store can be used to create sessions, query the configuration of Marten, generate schema migrations, and do bulk inserts.
1. `IDocumentSession` with a *Scoped* lifetime for all read and write operations. By default, this is done with the `IDocumentStore.OpenSession()` method and the session created will have the identity map behavior
1. `IQuerySession` with a *Scoped* lifetime for all read operations against the document store.

For more information, see:

* [Dependency injection in ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-3.1) for more explanation about the service lifetime behavior.
* Check [identity map mechanics](/guide/documents/advanced/identity-map) for an explanation of Marten session behavior
* Check [storing documents and unit of work](/guide/documents/basics/persisting) for session basics

## AddMarten() Usages

::: tip INFO
All the examples in this page are assuming the usage of the `IServiceCollection` interface for service
registrations within a `Startup` class, but Marten can be used with any IoC container or with no IoC container whatsoever.
:::

First, if you are using Marten completely out of the box with no customizations (besides attributes on your documents), you can just supply a connection string to the underlying Postgresql database like this:

<!-- snippet: sample_AddMartenByConnectionString -->
<a id='snippet-sample_addmartenbyconnectionstring'></a>
```cs
public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {

        var connectionString = Configuration.GetConnectionString("postgres");

        // By only the connection string
        services.AddMarten(connectionString);
    }

    // And other methods we don't care about here...
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/ByConnectionString/Startup.cs#L7-L29' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenbyconnectionstring' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The second option is to supply a [nested closure](https://martinfowler.com/dslCatalog/nestedClosure.html) to configure Marten inline like so:

<!-- snippet: sample_AddMartenByNestedClosure -->
<a id='snippet-sample_addmartenbynestedclosure'></a>
```cs
public class Startup
{
    public IConfiguration Configuration { get; }
    public IHostEnvironment Hosting { get; }

    public Startup(IConfiguration configuration, IHostEnvironment hosting)
    {
        Configuration = configuration;
        Hosting = hosting;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = Configuration.GetConnectionString("postgres");

        services.AddMarten(opts =>
        {
            opts.Connection(connectionString);

            // Use the more permissive schema auto create behavior
            // while in development
            if (Hosting.IsDevelopment())
            {
                opts.AutoCreateSchemaObjects = AutoCreate.All;
            }
        });
    }

    // And other methods we don't care about here...
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/ByNestedClosure/Startup.cs#L9-L40' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenbynestedclosure' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Lastly, if you prefer, you can pass a Marten `StoreOptions` object to `AddMarten()` like this example:

<!-- snippet: sample_AddMartenByStoreOptions -->
<a id='snippet-sample_addmartenbystoreoptions'></a>
```cs
public class Startup
{
    public IConfiguration Configuration { get; }
    public IHostEnvironment Hosting { get; }

    public Startup(IConfiguration configuration, IHostEnvironment hosting)
    {
        Configuration = configuration;
        Hosting = hosting;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        var options = BuildStoreOptions();

        services.AddMarten(options);
    }

    private StoreOptions BuildStoreOptions()
    {
        var connectionString = Configuration.GetConnectionString("postgres");

        // Or lastly, build a StoreOptions object yourself
        var options = new StoreOptions();
        options.Connection(connectionString);

        // Use the more permissive schema auto create behavior
        // while in development
        if (Hosting.IsDevelopment())
        {
            options.AutoCreateSchemaObjects = AutoCreate.All;
        }

        return options;
    }

    // And other methods we don't care about here...
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/ByStoreOptions/Startup.cs#L9-L48' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenbystoreoptions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The last option may be best for more complicated Marten configuration just to keep the configuration code cleaner as `Startup` classes can become convoluted.


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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/ConfiguringSessionCreation/Startup.cs#L10-L38' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customsessionfactory' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To register the custom session factory, use the `BuildSessionsWith()` method as shown in this example:

<!-- snippet: sample_AddMartenWithCustomSessionCreation -->
<a id='snippet-sample_addmartenwithcustomsessioncreation'></a>
```cs
public class Startup
{
    public Startup(IConfiguration configuration, IHostEnvironment hosting)
    {
        Configuration = configuration;
        Hosting = hosting;
    }

    public IConfiguration Configuration { get; }
    public IHostEnvironment Hosting { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = Configuration.GetConnectionString("postgres");

        services.AddMarten(opts =>
            {
                opts.Connection(connectionString);

                // Use the more permissive schema auto create behavior
                // while in development
                if (Hosting.IsDevelopment())
                {
                    opts.AutoCreateSchemaObjects = AutoCreate.All;
                }
            })

            // Chained helper to replace the built in
            // session factory behavior
            .BuildSessionsWith<CustomSessionFactory>();
    }

    // And other methods we don't care about here...
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/ConfiguringSessionCreation/Startup.cs#L40-L77' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenwithcustomsessioncreation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The session factories can also be used to build out and attach custom `IDocumentSessionListener` objects or replace the logging as we'll see in the next section.

See [diagnostics and instrumentation](/guide/documents/diagnostics) for more information.

## Customizing Session Creation by Scope

From a recent user request to Marten, what if you want to log the database statement activity in Marten with some kind of correlation to the active HTTP request or service bus message or some other logical
session identification in your application? That's now possible by using a custom `ISessionFactory`.

Taking the example of an ASP.Net Core application, let's say that you have a small service scoped to an HTTP request that tracks a correlation identifier for the request like this:

<!-- snippet: sample_CorrelationIdWithISession -->
<a id='snippet-sample_correlationidwithisession'></a>
```cs
public interface ISession
{
    Guid CorrelationId { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/PerScopeSessionCreation/Startup.cs#L14-L19' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_correlationidwithisession' title='Start of snippet'>anchor</a></sup>
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

    public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
    {
        // Do some kind of logging using the correlation id of the ISession
    }

    public void OnBeforeExecute(NpgsqlCommand command)
    {

    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/PerScopeSessionCreation/Startup.cs#L21-L53' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_correlatedmartenlogger' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/PerScopeSessionCreation/Startup.cs#L56-L88' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customsessionfactorybyscope' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Lastly, let's register our new session factory, but this time we need to take care to register the session factory as `Scoped` in the underlying container so we're using the correct `ISession` at runtime:

<!-- snippet: sample_AddMartenWithCustomSessionCreationByScope -->
<a id='snippet-sample_addmartenwithcustomsessioncreationbyscope'></a>
```cs
public class Startup
{
    public IConfiguration Configuration { get; }
    public IHostEnvironment Hosting { get; }

    public Startup(IConfiguration configuration, IHostEnvironment hosting)
    {
        Configuration = configuration;
        Hosting = hosting;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = Configuration.GetConnectionString("postgres");

        services.AddMarten(opts =>
        {
            opts.Connection(connectionString);

            // Use the more permissive schema auto create behavior
            // while in development
            if (Hosting.IsDevelopment())
            {
                opts.AutoCreateSchemaObjects = AutoCreate.All;
            }
        })

            // Chained helper to replace the CustomSessionFactory
            .BuildSessionsPerScopeWith<ScopedSessionFactory>();
    }

    // And other methods we don't care about here...
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/PerScopeSessionCreation/Startup.cs#L90-L124' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenwithcustomsessioncreationbyscope' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
This correlation tracking might be better with structural logging with something like [Serilog](https://serilog.net), but we'll leave that to users.
:::

## Eager Initialization of the DocumentStore

Lastly, if desirable, you can force Marten to initialize the applications document store as part of bootstrapping instead of waiting for it to be initialized on the first usage with this syntax:

<!-- snippet: sample_AddMartenWithEagerInitialization -->
<a id='snippet-sample_addmartenwitheagerinitialization'></a>
```cs
public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {

        var connectionString = Configuration.GetConnectionString("postgres");

        // By only the connection string
        services.AddMarten(connectionString)

            // Spin up the DocumentStore right this second!
            .InitializeStore();
    }

    // And other methods we don't care about here...
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/EagerInitialization/Startup.cs#L7-L32' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenwitheagerinitialization' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
