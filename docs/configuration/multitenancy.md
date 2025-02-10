# Multi-Tenancy with Database per Tenant

:::tip
Marten V5.0 largely rewired the internals to be aware of multiple databases in features such as the database cleaning,
the async projection daemon, and the database migrations.
:::

Marten V5.0 introduced (finally) built in support for multi-tenancy through separate databases per tenant or a group of tenants.

First off, let's try to answer the obvious questions you probably have:

* *Can I combine [conjoined multi-tenancy](/documents/multi-tenancy) and database per tenant?* - That's a **yes**.  
* *Does Marten know how to handle database migrations with multiple databases?* - Yes, and that was honestly most of the work to support this functionality:(
* *Will the [async daemon](/events/projections/async-daemon) work with multiple databases?* - Yes, and there's nothing else you need to do to enable that on the async daemon side
* *What strategies does Marten support out of the box for this?* - That's explained in the next two sections below.
* *If Marten doesn't do what I need for this feature, can I plug in my own strategy?* - That's also a yes, see the section on writing your own.
* *Does the `IDocumentStore.Advanced` features work for multiple databases?* - This is a little more complicated, but the answer is still yes. See the very last section on administering databases.
* *Can this strategy use different database schemas in the same database?* - **That's a hard no.** The databases have to be identical in all structures.

## Tenant Id Case Sensitivity

Hey, we've all been there. Our perfectly crafted code fails because of a @#$%#@%ing case sensitivity string comparison.
That's unfortunately happened to Marten users with the `tenantId` values passed into Marten, and it's likely to happen
again. To guard against that, you can force Marten to convert all supplied tenant ids from the outside world to either
upper or lower case to try to stop these kinds of case sensitivity bugs in their tracks like so:

<!-- snippet: sample_using_tenant_id_style -->
<a id='snippet-sample_using_tenant_id_style'></a>
```cs
var store = DocumentStore.For(opts =>
{
    // This is the default
    opts.TenantIdStyle = TenantIdStyle.CaseSensitive;

    // Or opt into this behavior:
    opts.TenantIdStyle = TenantIdStyle.ForceLowerCase;

    // Or force all tenant ids to be converted to upper case internally
    opts.TenantIdStyle = TenantIdStyle.ForceUpperCase;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MultiTenancy.cs#L14-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_tenant_id_style' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Static Database to Tenant Mapping

::: info
This is a simple option for a static number of tenant databases that may or may not be housed in the same physical PostgreSQL
instance. Marten does not automatically create the databases themselves.
:::

The first and simplest option built in is the `MultiTenantedDatabases()` syntax that assumes that all tenant databases are built upfront
and there is no automatic database provisioning at runtime. In this case, you can supply the mapping of databases to tenant id as shown
in the following code sample:

<!-- snippet: sample_using_multi_tenanted_databases -->
<a id='snippet-sample_using_multi_tenanted_databases'></a>
```cs
_host = await Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddMarten(opts =>
            {
                // Explicitly map tenant ids to database connection strings
                opts.MultiTenantedDatabases(x =>
                {
                    // Map multiple tenant ids to a single named database
                    x.AddMultipleTenantDatabase(db1ConnectionString, "database1")
                        .ForTenants("tenant1", "tenant2");

                    // Map a single tenant id to a database, which uses the tenant id as well for the database identifier
                    x.AddSingleTenantDatabase(tenant3ConnectionString, "tenant3");
                    x.AddSingleTenantDatabase(tenant4ConnectionString, "tenant4");
                });

                opts.RegisterDocumentType<User>();
                opts.RegisterDocumentType<Target>();
            })

            // All detected changes will be applied to all
            // the configured tenant databases on startup
            .ApplyAllDatabaseChangesOnStartup();
    }).StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/MultiTenancyTests/using_static_database_multitenancy.cs#L50-L79' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_multi_tenanted_databases' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Single Instance Multi-Tenancy

::: info
This might be the simplest possible way to get started with multi-tenancy per database. In only this case, Marten is able
to build any missing tenant databases based on the tenant id.
:::

The second out of the box option is to use a separate named database in the same database instance for each individual tenant. In this case, Marten is able to provision new tenant databases on the fly when a new tenant id is encountered for the first time. That will obviously depend on the application having sufficient permissions for this to work. We think this option may be mostly suitable for development and automated testing rather than production usage.

This usage is shown below:

<!-- snippet: sample_using_single_server_multi_tenancy -->
<a id='snippet-sample_using_single_server_multi_tenancy'></a>
```cs
_host = await Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddMarten(opts =>
        {
            opts
                // You have to specify a connection string for "administration"
                // with rights to provision new databases on the fly
                .MultiTenantedWithSingleServer(
                    ConnectionSource.ConnectionString,
                    t => t
                        // You can map multiple tenant ids to a single named database
                        .WithTenants("tenant1", "tenant2").InDatabaseNamed("database1")

                        // Just declaring that there are additional tenant ids that should
                        // have their own database
                        .WithTenants("tenant3", "tenant4") // own database
                );

            opts.RegisterDocumentType<User>();
            opts.RegisterDocumentType<Target>();
        }).ApplyAllDatabaseChangesOnStartup();
    }).StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/MultiTenancyTests/using_per_database_multitenancy.cs#L96-L123' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_single_server_multi_tenancy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Master Table Tenancy Model

::: info
Use this option if you have any need to add new tenant databases at runtime without incurring any application downtime.
This option may also be easier to maintain than the static mapped option if the number of tenant databases grows large, but that's
going to be a matter of preference and taste rather than any hard technical reasoning
:::

New in Marten 7.0 is a built in recipe for database multi-tenancy that allows for new tenant database to be discovered at
runtime using this syntax option:

<!-- snippet: sample_master_table_multi_tenancy -->
<a id='snippet-sample_master_table_multi_tenancy'></a>
```cs
using var host = await Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddMarten(sp =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var masterConnection = configuration.GetConnectionString("master");
                var options = new StoreOptions();

                // This is opting into a multi-tenancy model where a database table in the
                // master database holds information about all the possible tenants and their database connection
                // strings
                options.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
                {
                    x.ConnectionString = masterConnection;

                    // You can optionally configure the schema name for where the mt_tenants
                    // table is stored
                    x.SchemaName = "tenants";

                    // If set, this will override the database schema rules for
                    // only the master tenant table from the parent StoreOptions
                    x.AutoCreate = AutoCreate.CreateOrUpdate;

                    // Optionally seed rows in the master table. This may be very helpful for
                    // testing or local development scenarios
                    // This operation is an "upsert" upon application startup
                    x.RegisterDatabase("tenant1", configuration.GetConnectionString("tenant1"));
                    x.RegisterDatabase("tenant2", configuration.GetConnectionString("tenant2"));
                    x.RegisterDatabase("tenant3", configuration.GetConnectionString("tenant3"));

                    // Tags the application name to all the used connection strings as a diagnostic
                    // Default is the name of the entry assembly for the application or "Marten" if
                    // .NET cannot determine the entry assembly for some reason
                    x.ApplicationName = "MyApplication";
                });

                // Other Marten configuration

                return options;
            })
            // All detected changes will be applied to all
            // the configured tenant databases on startup
            .ApplyAllDatabaseChangesOnStartup();;
    }).StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/MultiTenancyExamples.cs#L15-L63' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_master_table_multi_tenancy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

With this model, Marten is setting up a table named `mt_tenant_databases` to store with just two columns:

1. `tenant_id`
2. `connection_string`

At runtime, when you ask for a new session for a specific tenant like so:

```csharp
using var session = store.LightweightSession("tenant1");
```

This new Marten tenancy strategy will first look for a database with the “tenant1” identifier its own memory, 
and if it’s not found, will try to reach into the database table to “find” the connection string for this 
newly discovered tenant. If a record is found, the new tenancy strategy caches the information, and proceeds just like normal.

Now, let me try to anticipate a couple questions you might have here:

* **Can Marten track and apply database schema changes to new tenant databases at runtime?** Yes, Marten does the schema check tracking on a database by database basis. This means that if you add a new tenant database to that underlying table, Marten will absolutely be able to make schema changes as needed to just that tenant database regardless of the state of other tenant databases.
* **Will the Marten command line tools recognize new tenant databases?** Yes, same thing. If you call dotnet run -- marten-apply for example, Marten will do the schema migrations independently for each tenant database, so any outstanding changes will be performed on each tenant database.
* **Can Marten spin up asynchronous projections for a new tenant database without requiring downtime?** Yes! Check out this big ol’ integration test proving that the new Marten V7 version of the async daemon can handle that just fine:

```csharp
[Fact]
public async Task add_tenant_database_and_verify_the_daemon_projections_are_running()
{
    // In this code block, I'm adding new tenant databases to the system that I
    // would expect Marten to discover and start up an asynchronous projection
    // daemon for all three newly discovered databases
    var tenancy = (MasterTableTenancy)theStore.Options.Tenancy;
    await tenancy.AddDatabaseRecordAsync("tenant1", tenant1ConnectionString);
    await tenancy.AddDatabaseRecordAsync("tenant2", tenant2ConnectionString);
    await tenancy.AddDatabaseRecordAsync("tenant3", tenant3ConnectionString);
 
    // This is a new service in Marten specifically to help you interrogate or
    // manipulate the state of running asynchronous projections within the current process
    var coordinator = _host.Services.GetRequiredService<IProjectionCoordinator>();
    var daemon1 = await coordinator.DaemonForDatabase("tenant1");
    var daemon2 = await coordinator.DaemonForDatabase("tenant2");
    var daemon3 = await coordinator.DaemonForDatabase("tenant3");
 
    // Just proving that the configured projections for the 3 new databases
    // are indeed spun up and running after Marten's new daemon coordinator
    // "finds" the new databases
    await daemon1.WaitForShardToBeRunning("TripCustomName:All", 30.Seconds());
    await daemon2.WaitForShardToBeRunning("TripCustomName:All", 30.Seconds());
    await daemon3.WaitForShardToBeRunning("TripCustomName:All", 30.Seconds());
}
```

At runtime, if the Marten V7 version of the async daemon (our sub system for building asynchronous projections constantly in a background IHostedService) is constantly doing “health checks” to make sure that *some process* is running all known asynchronous projections on all known client databases. Long story, short, Marten 7 is able to detect new tenant databases and spin up the asynchronous projection handling for these new tenants with zero downtime.

## Dynamically applying changes to tenants databases

If you didn't call the `ApplyAllDatabaseChangesOnStartup` method, Marten would still try to create a database [upon the session creation](/documents/sessions). This action is invasive and can cause issues like timeouts, cold starts, or deadlocks. It also won't apply all defined changes upfront (so, e.g. [indexes](/documents/indexing/), [custom schema extensions](/schema/extensions)).

If you don't know the tenant upfront, you can create and apply changes dynamically by:

<!-- snippet: sample_manual_single_tenancy_apply_changes -->
<a id='snippet-sample_manual_single_tenancy_apply_changes'></a>
```cs
var tenant = await theStore.Tenancy.GetTenantAsync(tenantId);
await tenant.Database.ApplyAllConfiguredChangesToDatabaseAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/adding_custom_schema_objects.cs#L151-L156' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_manual_single_tenancy_apply_changes' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can place this code somewhere in the tenant initialization code. For instance:

* tenant setup procedure,
* dedicated API endpoint
* [custom session factory](/configuration/hostbuilder#customizing-session-creation-globally), although that's not recommended for the reasons mentioned above. 

## Write your own tenancy strategy!

:::tip
It is strongly recommended that you first refer to the existing Marten options for per-database multi-tenancy before you write your own model. There are several helpers in the Marten codebase that will hopefully make this task easier. Failing all else, please feel free to ask questions in the [Marten's Discord channel](https://discord.gg/WMxrvegf8H) about custom multi-tenancy strategies.
:::

The multi-tenancy strategy is pluggable. Start by implementing the `Marten.Storage.ITenancy` interface:

<!-- snippet: sample_ITenancy -->
<a id='snippet-sample_itenancy'></a>
```cs
/// <summary>
///     Pluggable interface for Marten multi-tenancy by database
/// </summary>
public interface ITenancy: IDatabaseSource, IDisposable, IDatabaseUser
{
    /// <summary>
    ///     The default tenant. This can be null.
    /// </summary>
    Tenant Default { get; }

    /// <summary>
    ///     A composite document cleaner for the entire collection of databases
    /// </summary>
    IDocumentCleaner Cleaner { get; }

    /// <summary>
    ///     Retrieve or create a Tenant for the tenant id.
    /// </summary>
    /// <param name="tenantId"></param>
    /// <exception cref="UnknownTenantIdException"></exception>
    /// <returns></returns>
    Tenant GetTenant(string tenantId);

    /// <summary>
    ///     Retrieve or create a tenant for the tenant id
    /// </summary>
    /// <param name="tenantId"></param>
    /// <returns></returns>
    ValueTask<Tenant> GetTenantAsync(string tenantId);

    /// <summary>
    ///     Find or create the named database
    /// </summary>
    /// <param name="tenantIdOrDatabaseIdentifier"></param>
    /// <returns></returns>
    ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier);

    /// <summary>
    ///  Asserts that the requested tenant id is part of the current database
    /// </summary>
    /// <param name="database"></param>
    /// <param name="tenantId"></param>
    bool IsTenantStoredInCurrentDatabase(IMartenDatabase database, string tenantId);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Storage/ITenancy.cs#L19-L66' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_itenancy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Assuming that we have a custom `ITenancy` model:

<!-- snippet: sample_MySpecialTenancy -->
<a id='snippet-sample_myspecialtenancy'></a>
```cs
// Make sure you implement the Dispose() method and
// dispose all MartenDatabase objects
public class MySpecialTenancy: ITenancy
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/MultiTenancyTests/using_per_database_multitenancy.cs#L29-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_myspecialtenancy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

We can utilize that by applying that model at configuration time:

<!-- snippet: sample_apply_custom_tenancy -->
<a id='snippet-sample_apply_custom_tenancy'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("connection string");

    // Apply custom tenancy model
    opts.Tenancy = new MySpecialTenancy();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/MultiTenancyTests/using_per_database_multitenancy.cs#L81-L91' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_apply_custom_tenancy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Administering Multiple Databases

We've tried to make Marten support all the existing `IDocumentStore.Advanced` features either across all databases at one time where possible, or exposed a mechanism to access only one database at a time as shown below:

<!-- snippet: sample_administering_multiple_databases -->
<a id='snippet-sample_administering_multiple_databases'></a>
```cs
// Apply all detected changes in every known database
await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

// Only apply to the default database if not using multi-tenancy per
// database
await store.Storage.Database.ApplyAllConfiguredChangesToDatabaseAsync();

// Find a specific database
var database = await store.Storage.FindOrCreateDatabase("tenant1");

// Tear down everything
await database.CompletelyRemoveAllAsync();

// Check out the projection state in just this database
var state = await database.FetchEventStoreStatistics();

// Apply all outstanding database changes in just this database
await database.ApplyAllConfiguredChangesToDatabaseAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/MultiTenancyTests/using_static_database_multitenancy.cs#L210-L231' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_administering_multiple_databases' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

All remaining methods on `IDocumentStore.Advanced` apply to all databases.
