# Getting Started

Click this [link](https://sec.ch9.ms/ch9/2d29/a281311a-76bb-4573-a2a0-2dd7affc2d29/S315dotNETconf_high.mp4) to watch an introductory video on Marten.

First, go get the Marten library from Nuget:

Using .NET CLI

```shell
dotnet add package Marten
```

Or, using PowerShell

```powershell
PM> Install-Package Marten
```

Or, using paket:

```shell
paket add nuget Marten
```

The next step is to get access to a PostgreSQL **9.6+** database schema. If you want to let Marten build database schema objects on the fly at development time,
make sure that your user account has rights to execute `CREATE TABLE/FUNCTION` statements.

Marten uses the [Npgsql](http://www.npgsql.org) library to access PostgreSQL from .NET, so you'll likely want to read their [documentation on connection string syntax](http://www.npgsql.org/doc/connection-string-parameters.html).


## .NET version compatibility

Marten aligns with the [.NET Core Support Lifecycle](https://dotnet.microsoft.com/platform/support/policy/dotnet-core) to determine platform compatibility.

4.xx targets `netstandard2.0` & `net5.0` and is compatible with `.NET Core 2.x`, `.NET Core 3.x` and `.NET 5+`.

::: tip INFO
.NET Framework support was dropped as part of the v4 release, if you require .NET Framework support, please use the latest Marten 3.xx release.
:::

## .Net Core app integration

::: tip INFO
There's a very small [sample project in the Marten codebase](https://github.com/JasperFx/marten/tree/master/src/AspNetCoreWithMarten) that shows the mechanics for wiring
Marten into a .Net Core application.
:::

By popular demand, Marten 3.12 added extension methods to quickly integrate Marten into any .Net Core application that uses the `IServiceCollection` abstractions to register IoC services.

In the `Startup.ConfigureServices()` method of your .Net Core application (or you can use `IHostBuilder.ConfigureServices()` as well) make a call to `AddMarten()` to register Marten services like so:

<!-- snippet: sample_StartupConfigureServices -->
<!-- endSnippet -->

See [integrating Marten in .NET Core applications](/guide/integration) for more information and options about this integration.


## Bootstrapping a Document Store

To start up Marten in a running application, you need to create a single `IDocumentStore` object. The quickest way is to start with
all the default behavior and a connection string:

<!-- snippet: sample_start_a_store -->
<!-- endSnippet -->

Now, for your first document type, let's represent the users in our system:

<!-- snippet: sample_user_document -->
<!-- endSnippet -->

_For more information on document id's, see [identity](/guide/documents/identity/)._

And now that we've got a PostgreSQL schema and an `IDocumentStore`, let's start persisting and loading user documents:

<!-- snippet: sample_opening_sessions -->
<!-- endSnippet -->

## IoC container integration

::: tip INFO
Lamar supports the .Net Core abstractions for IoC service registrations, so you *could* happily
use the `AddMarten()` method directly with Lamar.
:::

The Marten team has striven to make the library perfectly usable without the usage of an IoC container, but you may still want to
use an IoC container specifically to manage dependencies and the life cycle of Marten objects.

Using [Lamar](https://jasperfx.github.io/lamar) as the example container, we recommend registering Marten something like this:

<!-- snippet: sample_MartenServices -->
<!-- endSnippet -->

There are really only two key points here:

1. There should only be one `IDocumentStore` object instance created in your application, so I scoped it as a "Singleton" in the StructureMap container
1. The `IDocumentSession` service that you use to read and write documents should be scoped as "one per transaction." In typical usage, this
   ends up meaning that an `IDocumentSession` should be scoped to a single HTTP request in web applications or a single message being handled in service
   bus applications.

There is a lot more capabilities than what we're showing here, so head on over to the table of contents on the sidebar to see what else Marten offers.

## Create database

Marten can be configured to create (or drop & create) databases in case they do not exist. This is done via store options, through `StoreOptions.CreateDatabasesForTenants`.

<!-- snippet: sample_marten_create_database -->
<!-- endSnippet -->

Databases are checked for existence upon store initialization. By default, connection attempts are made against the databases specified for tenants. If a connection attempt results in an invalid catalog error (3D000), database creation is triggered. `ITenantDatabaseCreationExpressions.CheckAgainstPgDatabase` can be used to alter this behaviour to check for database existence from `pg_database`.

Note that database creation requires the CREATEDB privilege. See PostgreSQL [CREATE DATABASE](https://www.postgresql.org/docs/current/static/sql-createdatabase.html) documentation for more.
