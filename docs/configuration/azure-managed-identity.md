---
description: Connect Marten to Azure Database for PostgreSQL using Microsoft Entra ID (managed identity) authentication with a periodically refreshed access token.
---

# Azure Database for PostgreSQL with Entra ID

[Azure Database for PostgreSQL flexible server](https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/concepts-azure-ad-authentication) can authenticate database connections with Microsoft Entra ID (formerly Azure AD) instead of a stored password. In that model there is no long-lived password at all: the "password" is an Entra access token that expires roughly every 60 to 90 minutes and has to be re-acquired for new connections.

A static connection string cannot do that — `opts.Connection("Host=...;Password=...")` bakes the credential in once at bootstrapping time. The supported way to plug a rotating credential into Marten is to build an [NpgsqlDataSource](https://www.npgsql.org/doc/basic-usage.html#data-source) yourself with Npgsql's `UsePeriodicPasswordProvider()`, then hand that data source to Marten through `UseNpgsqlDataSource()` or `opts.Connection(NpgsqlDataSource)` (see [Bootstrapping Marten](/configuration/hostbuilder#npgsqldatasource)). The data source owns connection pooling *and* token refresh, and Marten simply opens connections from it.

## Azure prerequisites

1. An Azure Database for PostgreSQL flexible server with Entra ID authentication enabled.
2. A managed identity (or service principal) that has been granted a PostgreSQL role on the server via the `pgaadauth` functions, e.g. `SELECT * FROM pgaadauth_create_principal('my-app-identity', false, false);` run by an Entra administrator.
3. A connection string whose `Username` is that role name — and no `Password` entry.

## Configuring the data source

Use `Azure.Identity` to acquire tokens and feed them to Npgsql as the password. The token audience (scope) for Azure Database for PostgreSQL is `https://ossrdbms-aad.database.windows.net/.default`:

```csharp
using Azure.Core;
using Azure.Identity;
using Npgsql;

// Token audience for Azure Database for PostgreSQL
const string TokenScope = "https://ossrdbms-aad.database.windows.net/.default";

var credential = new DefaultAzureCredential(
    new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityClientId });

// Username = the PostgreSQL role granted to the identity; no Password in the string
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UsePeriodicPasswordProvider(
    async (_, ct) =>
        (await credential.GetTokenAsync(new TokenRequestContext([TokenScope]), ct)).Token,

    // Entra tokens live ~60-90 minutes, so refresh comfortably inside that window
    successRefreshInterval: TimeSpan.FromMinutes(50),
    failureRefreshInterval: TimeSpan.FromSeconds(5));

builder.Services.AddSingleton(dataSourceBuilder.Build());

builder.Services.AddMarten(opts =>
    {
        // All the normal Marten configuration, but *no* opts.Connection() call --
        // the connection comes from the registered NpgsqlDataSource
        opts.DatabaseSchemaName = "myapp";
    })
    .UseLightweightSessions()
    .UseNpgsqlDataSource();
```

Npgsql calls the password provider on the schedule you give it and stamps the current token onto new physical connections, so pooled connections keep working as tokens roll over. `ManagedIdentityClientId` is only needed when the host has more than one user-assigned identity; `DefaultAzureCredential` also lets the same code run locally against your `az login` or Visual Studio credentials.

## Why register the data source in the container?

You could pass the built data source directly to `opts.Connection(dataSource)` and skip the container registration. Registering it as a singleton and using `UseNpgsqlDataSource()` is the better default though, because everything that builds your `IHost` gets the token provider for free — including the [command line tooling](/configuration/cli). `dotnet run -- db-apply`, `db-assert`, and `resources setup` all bootstrap the same host, so schema management against an Entra-only server works without any extra wiring.

## Sharing the data source with ancillary stores

`UseNpgsqlDataSource()` applies to the default store from `AddMarten()`. [Ancillary stores](/configuration/hostbuilder#ancillary-marten-stores) registered with `AddMartenStore<T>()` can share the exact same data source instance — same pool, same token provider:

```csharp
var dataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(dataSource);

builder.Services.AddMarten(opts => { /* main store configuration */ })
    .UseLightweightSessions()
    .UseNpgsqlDataSource();

builder.Services.AddMartenStore<IInvoicingStore>(opts =>
{
    opts.Connection(dataSource);
    opts.DatabaseSchemaName = "invoicing";
});
```

## Data source ownership

Marten treats a caller-supplied `NpgsqlDataSource` — whether it arrives through `UseNpgsqlDataSource()` or `opts.Connection(NpgsqlDataSource)` — as owned by the caller. Marten will never dispose it, so a single instance is safe to share across the main store, ancillary stores, and your own non-Marten usage. Dispose it yourself when the application shuts down (the container does this for you when the data source is registered as a singleton).

## Limitation: database provisioning

Marten's tenant database provisioning (`StoreOptions.CreateDatabasesForTenants` and its `MaintenanceDatabase()` option, see [database management](/schema/#create-database)) builds plain `NpgsqlConnection` objects from connection strings and does not flow the data source or its token provider. On a server that only accepts Entra ID authentication, that provisioning path cannot log in on its own — create the databases through your own maintenance connection (or infrastructure tooling) instead, and let Marten manage the schema objects within them.
