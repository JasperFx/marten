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

## Static Database to Tenant Mapping

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
                    x.AddMultipleTenantDatabase(db1ConnectionString,"database1").ForTenants("tenant1", "tenant2");

                    // Map a single tenant id to a database, which uses the tenant id as well for the database identifier
                    x.AddSingleTenantDatabase(tenant3ConnectionString, "tenant3");
                    x.AddSingleTenantDatabase(tenant4ConnectionString,"tenant4");
                });

                opts.RegisterDocumentType<User>();
                opts.RegisterDocumentType<Target>();

            })

            // All detected changes will be applied to all
            // the configured tenant databases on startup
            .ApplyAllDatabaseChangesOnStartup();
    }).StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/DatabaseMultiTenancy/using_static_database_multitenancy.cs#L49-L78' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_multi_tenanted_databases' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Single Instance Multi-Tenancy

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
                .MultiTenantedWithSingleServer(ConnectionSource.ConnectionString)

                // You can map multiple tenant ids to a single named database
                .WithTenants("tenant1", "tenant2").InDatabaseNamed("database1")

                // Just declaring that there are additional tenant ids that should
                // have their own database
                .WithTenants("tenant3", "tenant4"); // own database

            opts.RegisterDocumentType<User>();
            opts.RegisterDocumentType<Target>();

        }).ApplyAllDatabaseChangesOnStartup();
    }).StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/DatabaseMultiTenancy/using_per_database_multitenancy.cs#L73-L99' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_single_server_multi_tenancy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Write your own tenancy strategy!

:::tip
It is strongly recommended that you first refer to the existing Marten options for per-database multi-tenancy
before you write your own model. There are several helpers in the Marten codebase that will hopefully make
this task easier. Failing all else, please feel free to ask questions in the Marten Gitter room about custom
multi-tenancy strategies.
:::

The multi-tenancy strategy is pluggable. Start by implementing the `Marten.Storage.ITenancy` interface:

<!-- snippet: sample_ITenancy -->
<a id='snippet-sample_itenancy'></a>
```cs
/// <summary>
/// Pluggable interface for Marten multi-tenancy by database
/// </summary>
public interface ITenancy : IDatabaseSource
{
    /// <summary>
    /// Retrieve or create a Tenant for the tenant id.
    /// </summary>
    /// <param name="tenantId"></param>
    /// <exception cref="UnknownTenantIdException"></exception>
    /// <returns></returns>
    Tenant GetTenant(string tenantId);

    /// <summary>
    /// The default tenant. This can be null.
    /// </summary>
    Tenant Default { get; }

    /// <summary>
    /// A composite document cleaner for the entire collection of databases
    /// </summary>
    IDocumentCleaner Cleaner { get; }

    /// <summary>
    /// Retrieve or create a tenant for the tenant id
    /// </summary>
    /// <param name="tenantId"></param>
    /// <returns></returns>

    ValueTask<Tenant> GetTenantAsync(string tenantId);

    /// <summary>
    /// Find or create the named database
    /// </summary>
    /// <param name="tenantIdOrDatabaseIdentifier"></param>
    /// <returns></returns>

    ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Storage/ITenancy.cs#L9-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_itenancy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Assuming that we have a custom `ITenancy` model:

<!-- snippet: sample_MySpecialTenancy -->
<a id='snippet-sample_myspecialtenancy'></a>
```cs
public class MySpecialTenancy: ITenancy
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/DatabaseMultiTenancy/using_per_database_multitenancy.cs#L27-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_myspecialtenancy' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/DatabaseMultiTenancy/using_per_database_multitenancy.cs#L58-L68' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_apply_custom_tenancy' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/DatabaseMultiTenancy/using_static_database_multitenancy.cs#L187-L208' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_administering_multiple_databases' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

All remaining methods on `IDocumentStore.Advanced` apply to all databases.
