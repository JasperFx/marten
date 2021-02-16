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

<<< @/../src/AspNetCoreWithMarten/Startup.cs#sample_StartupConfigureServices

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

<<< @/../src/AspNetCoreWithMarten/Samples/ByConnectionString/Startup.cs#sample_AddMartenByConnectionString

The second option is to supply a [nested closure](https://martinfowler.com/dslCatalog/nestedClosure.html) to configure Marten inline like so:

<<< @/../src/AspNetCoreWithMarten/Samples/ByNestedClosure/Startup.cs#sample_AddMartenByNestedClosure

Lastly, if you prefer, you can pass a Marten `StoreOptions` object to `AddMarten()` like this example:

<<< @/../src/AspNetCoreWithMarten/Samples/ByStoreOptions/Startup.cs#sample_AddMartenByStoreOptions

The last option may be best for more complicated Marten configuration just to keep the configuration code cleaner as `Startup` classes can become convoluted.


## Customizing Session Creation Globally

By default, Marten will create a document session with the basic identity map enabled and a [ReadCommitted](https://docs.microsoft.com/en-us/dotnet/api/system.transactions.isolationlevel?view=netcore-3.1) transaction isolation level. If you want to use a different configuration for sessions globally in your application, you can use a custom implementation of the `ISessionFactory` class
as shown in this example:

<<< @/../src/AspNetCoreWithMarten/Samples/ConfiguringSessionCreation/Startup.cs#sample_CustomSessionFactory

To register the custom session factory, use the `BuildSessionsWith()` method as shown in this example:

<<< @/../src/AspNetCoreWithMarten/Samples/ConfiguringSessionCreation/Startup.cs#sample_AddMartenWithCustomSessionCreation

The session factories can also be used to build out and attach custom `IDocumentSessionListener` objects or replace the logging as we'll see in the next section.

See [diagnostics and instrumentation](/guide/documents/diagnostics) for more information.

## Customizing Session Creation by Scope

From a recent user request to Marten, what if you want to log the database statement activity in Marten with some kind of correlation to the active HTTP request or service bus message or some other logical
session identification in your application? That's now possible by using a custom `ISessionFactory`.

Taking the example of an ASP.Net Core application, let's say that you have a small service scoped to an HTTP request that tracks a correlation identifier for the request like this:

<<< @/../src/AspNetCoreWithMarten/Samples/PerScopeSessionCreation/Startup.cs#sample_CorrelationIdWithISession

And a custom Marten session logger to add the correlation identifier to the log output like this:

<<< @/../src/AspNetCoreWithMarten/Samples/PerScopeSessionCreation/Startup.cs#sample_CorrelatedMartenLogger

Now, let's move on to building out a custom session factory that will attach our correlated marten logger to sessions being resolved from the IoC container:

<<< @/../src/AspNetCoreWithMarten/Samples/PerScopeSessionCreation/Startup.cs#sample_CustomSessionFactoryByScope

Lastly, let's register our new session factory, but this time we need to take care to register the session factory as `Scoped` in the underlying container so we're using the correct `ISession` at runtime:

<<< @/../src/AspNetCoreWithMarten/Samples/PerScopeSessionCreation/Startup.cs#sample_AddMartenWithCustomSessionCreationByScope

::: tip
This correlation tracking might be better with structural logging with something like [Serilog](https://serilog.net), but we'll leave that to users.
:::

## Eager Initialization of the DocumentStore

Lastly, if desirable, you can force Marten to initialize the applications document store as part of bootstrapping instead of waiting for it to be initialized on the first usage with this syntax:

<<< @/../src/AspNetCoreWithMarten/Samples/EagerInitialization/Startup.cs#sample_AddMartenWithEagerInitialization
