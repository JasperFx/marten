# Multi-Tenanted Documents

Marten supports multi-tenancy to provide data isolation between tenants, aka groups of users. In effect, this allows scoping storage operations, such as persisting and loading data, so that no tenant can access data of others. Marten provides multi-tenancy at the logical level, by associating data records with a tenant identifier. In addition, multi-tenancy through separate databases or schemas is planned.

By default, Marten operates in single-tenancy mode (`TenancyStyle.Single`) with multi-tenancy disabled.

Once configured for multi-tenancy, Marten exposes it via sessions (`IQuerySession`, `IDocumentSession`) scoped to specific tenants, as well as various overloads to saving operations that accept a tenant identifier.

## Scoping Sessions to Tenancy

The following sample demonstrates scoping a document session to tenancy identified as _tenant1_. With multi-tenancy enabled, the persisted `User` objects are then associated with the tenancy of the session.

<!-- snippet: sample_tenancy-scoping-session-write -->
<a id='snippet-sample_tenancy-scoping-session-write'></a>
```cs
// Write some User documents to tenant "tenant1"
using (var session = theStore.LightweightSession("tenant1"))
{
    session.Store(new User { Id = "u1", UserName = "Bill", Roles = new[] { "admin" } });
    session.Store(new User { Id = "u2", UserName = "Lindsey", Roles = new string[0] });
    session.SaveChanges();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Bugs/Bug_1884_multi_tenancy_and_Any_query.cs#L65-L75' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenancy-scoping-session-write' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_tenancy-scoping-session-write-1'></a>
```cs
// Write some User documents to tenant "tenant1"
using (var session = theStore.LightweightSession("tenant1"))
{
    session.Store(new User { Id = "u1", UserName = "Bill", Roles = new[] { "admin" } });
    session.Store(new User { Id = "u2", UserName = "Lindsey", Roles = new string[0] });
    session.SaveChanges();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Bugs/Bug_1884_multi_tenancy_and_Any_query.cs#L112-L122' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenancy-scoping-session-write-1' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_tenancy-scoping-session-write-2'></a>
```cs
// Write some User documents to tenant "tenant1"
using (var session = store.LightweightSession("tenant1"))
{
    session.Store(new User { UserName = "Bill" });
    session.Store(new User { UserName = "Lindsey" });
    session.SaveChanges();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MultiTenancy.cs#L55-L65' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenancy-scoping-session-write-2' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As with storing, the load operations respect tenancy of the session.

<!-- snippet: sample_tenancy-scoping-session-read -->
<a id='snippet-sample_tenancy-scoping-session-read'></a>
```cs
// When you query for data from the "tenant1" tenant,
// you only get data for that tenant
using (var query = store.QuerySession("tenant1"))
{
    query.Query<User>()
        .Select(x => x.UserName)
        .ToList()
        .ShouldHaveTheSameElementsAs("Bill", "Lindsey");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MultiTenancy.cs#L75-L87' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenancy-scoping-session-read' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Lastly, unlike reading operations, `IDocumentSession.Store` offers an overload to explicitly pass in a tenant identifier, bypassing any tenancy associated with the session. Similar overload for tenancy exists for `IDocumentStore.BulkInsert`.

## Default Tenancy

With multi-tenancy enabled, Marten associates each record with a tenancy. If no explicit tenancy is specified, either via policies, mappings, scoped sessions or overloads, Marten will default to `Tenancy.DefaultTenantId` with a constant value of `*DEFAULT*`.

The following sample demonstrates persisting documents as non-tenanted, under default tenant and other named tenants then querying them back in a session scoped to a specific named tenant and default tenant.

<!-- snippet: sample_tenancy-mixed-tenancy-non-tenancy-sample -->
<a id='snippet-sample_tenancy-mixed-tenancy-non-tenancy-sample'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.DatabaseSchemaName = "mixed_multi_tenants";
    opts.Connection(ConnectionSource.ConnectionString);
    opts.Schema.For<Target>().MultiTenanted(); // tenanted
    opts.Schema.For<User>(); // non-tenanted
    opts.Schema.For<Issue>().MultiTenanted(); // tenanted
});

store.Advanced.Clean.DeleteAllDocuments();

// Add documents to tenant Green
var greens = Target.GenerateRandomData(10).ToArray();
store.BulkInsert("Green", greens);

// Add documents to tenant Red
var reds = Target.GenerateRandomData(11).ToArray();
store.BulkInsert("Red", reds);

// Add non-tenanted documents
// User is non-tenanted in schema
var user1 = new User { UserName = "Frank" };
var user2 = new User { UserName = "Bill" };
store.BulkInsert(new[] { user1, user2 });

// Add documents to default tenant
// Note that schema for Issue is multi-tenanted hence documents will get added
// to default tenant if tenant is not passed in the bulk insert operation
var issue1 = new Issue { Title = "Test issue1" };
var issue2 = new Issue { Title = "Test issue2" };
store.BulkInsert(new[] { issue1, issue2 });

// Create a session with tenant Green
using (var session = store.QuerySession("Green"))
{
    // Query tenanted document as the tenant passed in session
    session.Query<Target>().Count().ShouldBe(10);

    // Query non-tenanted documents
    session.Query<User>().Count().ShouldBe(2);

    // Query documents in default tenant from a session using tenant Green
    session.Query<Issue>().Count(x => x.TenantIsOneOf(Tenancy.DefaultTenantId)).ShouldBe(2);

    // Query documents from tenant Red from a session using tenant Green
    session.Query<Target>().Count(x => x.TenantIsOneOf("Red")).ShouldBe(11);
}

// create a session without passing any tenant, session will use default tenant
using (var session = store.QuerySession())
{
    // Query non-tenanted documents
    session.Query<User>().Count().ShouldBe(2);

    // Query documents in default tenant
    // Note that session is using default tenant
    session.Query<Issue>().Count().ShouldBe(2);

    // Query documents on tenant Green
    session.Query<Target>().Count(x => x.TenantIsOneOf("Green")).ShouldBe(10);

    // Query documents on tenant Red
    session.Query<Target>().Count(x => x.TenantIsOneOf("Red")).ShouldBe(11);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/MultiTenancy/conjoined_multi_tenancy_with_partitioning.cs#L268-L335' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenancy-mixed-tenancy-non-tenancy-sample' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_tenancy-mixed-tenancy-non-tenancy-sample-1'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.DatabaseSchemaName = "mixed_multi_tenants";
    opts.Connection(ConnectionSource.ConnectionString);
    opts.Schema.For<Target>().MultiTenanted(); // tenanted
    opts.Schema.For<User>(); // non-tenanted
    opts.Schema.For<Issue>().MultiTenanted(); // tenanted
});

store.Advanced.Clean.DeleteAllDocuments();

// Add documents to tenant Green
var greens = Target.GenerateRandomData(10).ToArray();
store.BulkInsert("Green", greens);

// Add documents to tenant Red
var reds = Target.GenerateRandomData(11).ToArray();
store.BulkInsert("Red", reds);

// Add non-tenanted documents
// User is non-tenanted in schema
var user1 = new User { UserName = "Frank" };
var user2 = new User { UserName = "Bill" };
store.BulkInsert(new[] { user1, user2 });

// Add documents to default tenant
// Note that schema for Issue is multi-tenanted hence documents will get added
// to default tenant if tenant is not passed in the bulk insert operation
var issue1 = new Issue { Title = "Test issue1" };
var issue2 = new Issue { Title = "Test issue2" };
store.BulkInsert(new[] { issue1, issue2 });

// Create a session with tenant Green
using (var session = store.QuerySession("Green"))
{
    // Query tenanted document as the tenant passed in session
    session.Query<Target>().Count().ShouldBe(10);

    // Query non-tenanted documents
    session.Query<User>().Count().ShouldBe(2);

    // Query documents in default tenant from a session using tenant Green
    session.Query<Issue>().Count(x => x.TenantIsOneOf(Tenancy.DefaultTenantId)).ShouldBe(2);

    // Query documents from tenant Red from a session using tenant Green
    session.Query<Target>().Count(x => x.TenantIsOneOf("Red")).ShouldBe(11);
}

// create a session without passing any tenant, session will use default tenant
using (var session = store.QuerySession())
{
    // Query non-tenanted documents
    session.Query<User>().Count().ShouldBe(2);

    // Query documents in default tenant
    // Note that session is using default tenant
    session.Query<Issue>().Count().ShouldBe(2);

    // Query documents on tenant Green
    session.Query<Target>().Count(x => x.TenantIsOneOf("Green")).ShouldBe(10);

    // Query documents on tenant Red
    session.Query<Target>().Count(x => x.TenantIsOneOf("Red")).ShouldBe(11);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/MultiTenancy/conjoined_multi_tenancy.cs#L249-L316' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenancy-mixed-tenancy-non-tenancy-sample-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In some cases, You may want to disable using the default tenant for storing documents, set `StoreOptions.DefaultTenantUsageEnabled` to `false`. With this option disabled, Tenant (non-default tenant) should be passed via method argument or `SessionOptions` when creating a session using document store. Marten will throw an exception `DefaultTenantUsageDisabledException` if a session is created using default tenant.

## Querying Multi-Tenanted Documents

Inside the LINQ provider, when you open a session for a specific tenant like so:

<!-- snippet: sample_tenancy-scoping-session-read -->
<a id='snippet-sample_tenancy-scoping-session-read'></a>
```cs
// When you query for data from the "tenant1" tenant,
// you only get data for that tenant
using (var query = store.QuerySession("tenant1"))
{
    query.Query<User>()
        .Select(x => x.UserName)
        .ToList()
        .ShouldHaveTheSameElementsAs("Bill", "Lindsey");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MultiTenancy.cs#L75-L87' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenancy-scoping-session-read' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten will automatically filter the LINQ query for the current tenant _if the current document type is tenanted_. However, if
you want to query across multiple tenants or across documents for any tenant, you're still in luck with the `TenantIsOneOf()` LINQ
filter:

<!-- snippet: sample_tenant_is_one_of -->
<a id='snippet-sample_tenant_is_one_of'></a>
```cs
// query data for a selected list of tenants
var actual = await query.Query<Target>().Where(x => x.TenantIsOneOf("Green", "Red") && x.Flag)
    .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/MultiTenancy/conjoined_multi_tenancy_with_partitioning.cs#L412-L418' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenant_is_one_of' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_tenant_is_one_of-1'></a>
```cs
// query data for a selected list of tenants
var actual = await query.Query<Target>().Where(x => x.TenantIsOneOf("Green", "Red") && x.Flag)
    .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/MultiTenancy/conjoined_multi_tenancy.cs#L393-L399' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenant_is_one_of-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or the `AnyTenant()` filter:

<!-- snippet: sample_any_tenant -->
<a id='snippet-sample_any_tenant'></a>
```cs
// query data across all tenants
var actual = query.Query<Target>().Where(x => x.AnyTenant() && x.Flag)
    .OrderBy(x => x.Id).Select(x => x.Id).ToArray();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/MultiTenancy/conjoined_multi_tenancy_with_partitioning.cs#L357-L363' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_any_tenant' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_any_tenant-1'></a>
```cs
// query data across all tenants
var actual = query.Query<Target>().Where(x => x.AnyTenant() && x.Flag)
    .OrderBy(x => x.Id).Select(x => x.Id).ToArray();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/MultiTenancy/conjoined_multi_tenancy.cs#L338-L344' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_any_tenant-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Configuring Tenancy

The three levels of tenancy that Marten supports are expressed in the enum `TenancyStyle` with effective values of:

- `Single`, no multi-tenancy
- `Conjoined`, multi-tenancy through tenant id
- `Separate`, multi-tenancy through separate databases or schemas

Tenancy can be configured at the store level, applying to all documents or, at the most fine-grained level, on individual documents.

## Partitioning by Tenant <Badge type="tip" text="7.26" />

::: warning
This is an "opt in" model so as to not impact existing users. Moving from non-partitioned to partitioned tables
_may_ require some system downtime as this requires some potentially destructive changes to the database as Marten
will have to copy, drop, and recreate the document storage.
:::

If you are using conjoined multi-tenancy with Marten, you may be able to achieve a significant performance gain
by opting into [PostgreSQL table partitioning](https://www.postgresql.org/docs/current/ddl-partitioning.html) based on the tenant id. This can be a way to achieve greater scalability
without having to take on the extra deployment complexity that comes with multi-tenancy through separate databases. In effect,
this lets PostgreSQL store data in smaller, tenant specific table partitions.

To enable table partitioning by the tenant id for all document types, use this syntax:

<!-- snippet: sample_tenancy-configure-through-policy_with_partitioning -->
<a id='snippet-sample_tenancy-configure-through-policy_with_partitioning'></a>
```cs
storeOptions.Policies.AllDocumentsAreMultiTenantedWithPartitioning(x =>
{
    // Selectively by LIST partitioning
    x.ByList()
        // Adding explicit table partitions for specific tenant ids
        .AddPartition("t1", "T1")
        .AddPartition("t2", "T2");

    // OR Use LIST partitioning, but allow the partition tables to be
    // controlled outside of Marten by something like pg_partman
    // https://github.com/pgpartman/pg_partman
    x.ByExternallyManagedListPartitions();

    // OR Just spread out the tenant data by tenant id through
    // HASH partitioning
    // This is using three different partitions with the supplied
    // suffix names
    x.ByHash("one", "two", "three");

    // OR Partition by tenant id based on ranges of tenant id values
    x.ByRange()
        .AddRange("north_america", "na", "nazzzzzzzzzz")
        .AddRange("asia", "a", "azzzzzzzz");

    // OR use RANGE partitioning with the actual partitions managed
    // externally
    x.ByExternallyManagedRangePartitions();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MultiTenancy.cs#L112-L143' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenancy-configure-through-policy_with_partitioning' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To enable partitioning for a specific document type, use this option:

<!-- snippet: sample_configure_partitioning_on_single_table -->
<a id='snippet-sample_configure_partitioning_on_single_table'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    opts.Schema.For<User>().MultiTenantedWithPartitioning(x =>
    {
        x.ByExternallyManagedListPartitions();
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MultiTenancy.cs#L193-L205' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure_partitioning_on_single_table' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And lastly, if you need to use a mix of tenanted and global document types, but still want to use a consistent 
partitioning scheme for the document types that are tenanted, you have this option:

<!-- snippet: sample_multi_tenancy_partitioning_policy -->
<a id='snippet-sample_multi_tenancy_partitioning_policy'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // This document type is global, so no tenancy
    opts.Schema.For<Region>().SingleTenanted();

    // We want these document types to be tenanted
    opts.Schema.For<Invoice>().MultiTenanted();
    opts.Schema.For<User>().MultiTenanted();

    // Apply table partitioning by tenant id to each document type
    // that is using conjoined multi-tenancy
    opts.Policies.PartitionMultiTenantedDocuments(x =>
    {
        x.ByExternallyManagedListPartitions();
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MultiTenancy.cs#L210-L231' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_multi_tenancy_partitioning_policy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Tenancy Through Policies

Tenancy can be configured through Document Policies, accessible via `StoreOptions.Policies`. The following sample demonstrates setting the default tenancy to `TenancyStyle.Conjoined` for all documents.

<!-- snippet: sample_tenancy-configure-through-policy -->
<a id='snippet-sample_tenancy-configure-through-policy'></a>
```cs
storeOptions.Policies.AllDocumentsAreMultiTenanted();
// Shorthand for
// storeOptions.Policies.ForAllDocuments(_ => _.TenancyStyle = TenancyStyle.Conjoined);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MultiTenancy.cs#L44-L50' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenancy-configure-through-policy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Tenancy At Document Level & Policy Overrides

Tenancy can be configured at a document level through document mappings. This also enables overriding store-level configurations applied through Document Policies. The following sample demonstrates setting, through `StoreOptions` the tenancy for `Target` to `TenancyStyle.Conjoined`, making it deviate from the configured default policy of `TenancyStyle.Single`.

<!-- snippet: sample_tenancy-configure-override -->
<a id='snippet-sample_tenancy-configure-override'></a>
```cs
storeOptions.Policies.ForAllDocuments(x => x.TenancyStyle = TenancyStyle.Single);
storeOptions.Schema.For<Target>().MultiTenanted();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/document_policies.cs#L59-L64' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenancy-configure-override' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also do it the other way round, having the default set to `TenancyStyle.Conjoined` and overriding it to `TenancyStyle.Single` for `Target`.

<!-- snippet: sample_tenancy-configure-override-with-single-tenancy -->
<a id='snippet-sample_tenancy-configure-override-with-single-tenancy'></a>
```cs
storeOptions.Policies.ForAllDocuments(x => x.TenancyStyle = TenancyStyle.Conjoined);
storeOptions.Schema.For<Target>().SingleTenanted();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/document_policies.cs#L76-L81' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenancy-configure-override-with-single-tenancy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Marten-Managed Table Partitioning by Tenant <Badge type="tip" text="7.28" />

Man, that's a mouthful! So here's the situation. You have a large number of tenants, use the "conjoined" tenancy model,
and also want to use the table partitioning support as a way to improve performance in large databases. Marten has an 
option where you can store the valid tenant ids and what named partition that tenant id should be stored in to a database 
table (also Marten managed, because that's how we roll!). By using this Marten controlled storage, Marten is able to
dynamically create the right table partitions for each known tenant id for each known, "conjoined"/multi-tenanted
document storage.

Here's a sample of using this feature. First, the configuration is:

<!-- snippet: sample_configure_marten_managed_tenant_partitioning -->
<a id='snippet-sample_configure_marten_managed_tenant_partitioning'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));

    // Make all document types use "conjoined" multi-tenancy -- unless explicitly marked with
    // [SingleTenanted] or explicitly configured via the fluent interfce
    // to be single-tenanted
    opts.Policies.AllDocumentsAreMultiTenanted();

    // It's required to explicitly tell Marten which database schema to put
    // the mt_tenant_partitions table
    opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/MultiTenancyTests/marten_managed_tenant_id_partitioning.cs#L113-L130' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure_marten_managed_tenant_partitioning' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The tenant to partition name mapping will be stored in a table created by Marten called `mt_tenant_partitions` with
two columns:

1. `partition_name` -- really the partition table suffix name. If you want a one to one relationship, this is the tenant id
2. `partition_value`-- the value of the tenant id

Before the application is initialized, it's possible to load or delete data directly into these tables. At runtime, 
you can add new tenant id partitions with this helper API on `IDocumentStore.Advanced`:

<!-- snippet: sample_add_managed_tenants_at_runtime -->
<a id='snippet-sample_add_managed_tenants_at_runtime'></a>
```cs
await theStore
    .Advanced
    // This is ensuring that there are tenant id partitions for all multi-tenanted documents
    // with the named tenant ids
    .AddMartenManagedTenantsAsync(CancellationToken.None, "a1", "a2", "a3");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/MultiTenancyTests/marten_managed_tenant_id_partitioning.cs#L56-L64' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_add_managed_tenants_at_runtime' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The API above will try to add any missing table partitions to all known document types. There is also a separate overload
that will take a `Dictionary<string, string>` argument that maps tenant ids to a named partition suffix. This might 
be valuable if you frequently query for multiple tenants at one time. We think that the 1 to 1 tenant id to partition model
is a good default approach though. 

::: tip
Just like with the codegen-ahead model, you may want to tell Marten about all possible document types
upfront so that it is better able to add the partitions for each tenant id as needed.
:::

To exempt document types from having partitioned tables, such as for tables you expect to be so small that there's no value and maybe
even harm by partitioning, you can use either an attribute on the document type:

<!-- snippet: sample_using_DoNotPartitionAttribute -->
<a id='snippet-sample_using_donotpartitionattribute'></a>
```cs
[DoNotPartition]
public class DocThatShouldBeExempted1
{
    public Guid Id { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/MultiTenancyTests/marten_managed_tenant_id_partitioning.cs#L184-L192' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_donotpartitionattribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

or exempt a single document type through the fluent interface:

<!-- snippet: sample_exempt_from_partitioning_through_fluent_interface -->
<a id='snippet-sample_exempt_from_partitioning_through_fluent_interface'></a>
```cs
opts.Schema.For<DocThatShouldBeExempted2>().DoNotPartition();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/MultiTenancyTests/marten_managed_tenant_id_partitioning.cs#L169-L173' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_exempt_from_partitioning_through_fluent_interface' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Implementation Details

At the moment, Marten implements two modes of tenancy, namely single tenancy and conjoined multi-tenancy.

### Conjoined Tenancy

The conjoined (`TenancyStyle.Conjoined`) multi-tenancy in Marten is implemented by associating each record with a tenant identifier. As such, Marten does not guarantee or enforce data isolation via database access privileges.

#### Effects On Schema

Once enabled, `TenancyStyle.Conjoined` introduces a `tenant_id` column to Marten tables. This column, of type `varchar` with the default value of `*DEFAULT*` (default tenancy), holds the tenant identifier associated with the record. Furthermore, Marten creates an index on this column by default.

A unique index may optionally be scoped per tenant (see [unique indexes](/documents/indexing/unique)).
