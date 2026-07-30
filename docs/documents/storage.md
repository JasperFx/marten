# Database Storage

For each top level document type, Marten will generate database objects for:

* A database table called *mt_doc_[document alias]*, where the document alias is typically derived from the class name of the top level document type
* A function called *mt_upsert_[document alias]*
* A function called *mt_update_[document alias]*
* A function called *mt_insert_[document alias]*
* A function called *mt_overwrite_[document alias]*, an upsert function that bypasses any kind of configured optimistic concurrency checks

## Overriding the Database Schema

By default, all of the document type tables will be created and used from the *public* schema. That can be overridden globally with
this usage:

<!-- snippet: sample_setting_database_schema_name -->
<a id='snippet-sample_setting_database_schema_name'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");
    opts.DatabaseSchemaName = "other";
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDatabaseSchemaName.cs#L9-L17' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_setting_database_schema_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you choose, you can override the default database schema name for the `DocumentStore` by explicitly setting the schema for an individual document type through the `MartenRegistry` fluent interface like this:

<!-- snippet: sample_configure_schema_by_document_type -->
<a id='snippet-sample_configure_schema_by_document_type'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");
    opts.DatabaseSchemaName = "other";

    // This would take precedence for the
    // User document type storage
    opts.Schema.For<User>()
        .DatabaseSchemaName("users");
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDatabaseSchemaName.cs#L22-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure_schema_by_document_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or by using an attribute on your document type:

<!-- snippet: sample_using_databaseschemaname_attribute -->
<a id='snippet-sample_using_databaseschemaname_attribute'></a>
```cs
[DatabaseSchemaName("organization")]
public class Customer
{
    [Identity] public string Name { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/DocumentMappingTests.cs#L807-L815' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_databaseschemaname_attribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Type Aliases

In the not unlikely case that you need to disambiguate table storage for two or more documents with the same type name, you can override the type alias either programmatically with `MartenRegistry`:

<!-- snippet: sample_marten-registry-to-override-document-alias -->
<a id='snippet-sample_marten-registry-to-override-document-alias'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);

    _.Schema.For<User>().DocumentAlias("folks");
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/configuring_the_document_type_alias.cs#L26-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten-registry-to-override-document-alias' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

or by decorating the actual document class with an attribute:

<!-- snippet: sample_using-document-alias-attribute -->
<a id='snippet-sample_using-document-alias-attribute'></a>
```cs
[DocumentAlias("johndeere")]
public class Tractor
{
    public string id;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/configuring_the_document_type_alias.cs#L38-L44' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-document-alias-attribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Table Partitioning <Badge type="tip" text="7.26" />

::: warning
You may want to do manual database migrations if introducing partitioning into an existing database that does not
currently use partitioning as it may require some system downtime to rebuild the document or event storage. 
:::

Marten has some direct support for utilizing and managing [table partitioning](https://www.postgresql.org/docs/current/ddl-partitioning.html) with the underlying PostgreSQL database as
a way to optimize your application by letting PostgreSQL largely query against smaller tables when you commonly query against
a certain document member.

Marten allows you to define table partitions for:

* [Hot/Cold Storage in the Event Store](/events/optimizing.html) by the stream `IsArchived` property
* [Hot/Cold Storage for Soft Deleted Documents](/documents/deletes.html#partitioning-by-deleted-status)
* [Partitioning by Tenant Id for "Conjoined" Tenancy](/documents/multi-tenancy.html#partitioning-by-tenant)
* User defined partitioning based on a user selected member of a document (shown below)

In all cases, the table partitioning is:

1. 100% "opt in", meaning that you have to explicitly tell Marten to do the partitioning
2. Automatically migrated by Marten when the configured partitions are different than the actual database with all the normal
  Marten database migration tooling

To partition the storage for a document table on an arbitrary document member, use this syntax:

<!-- snippet: sample_configuring_partitioning_by_document_member -->
<a id='snippet-sample_configuring_partitioning_by_document_member'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Set up table partitioning for the User document type using RANGE partitioning
    opts.Schema.For<User>()
        .PartitionOn(x => x.Age, x =>
        {
            x.ByRange()
                .AddRange("young", 0, 20)
                .AddRange("twenties", 21, 29)
                .AddRange("thirties", 31, 39);
        });

    // Or use PostgreSQL HASH partitioning and split the users over multiple tables
    opts.Schema.For<User>()
        .PartitionOn(x => x.UserName, x =>
        {
            x.ByHash("one", "two", "three");
        });

    // Or use PostgreSQL LIST partitioning and split the users over multiple tables
    opts.Schema.For<Issue>()
        .PartitionOn(x => x.Status, x =>
        {
            // There is a default partition for anything that doesn't fall into
            // these specific values
            x.ByList()
                .AddPartition("completed", "Completed")
                .AddPartition("new", "New");
        });

    // Or use pg_partman to manage partitioning outside of Marten
    opts.Schema.For<User>()
        .PartitionOn(x => x.Age, x =>
        {
            x.ByExternallyManagedRangePartitions();

            // or instead with list

            x.ByExternallyManagedListPartitions();

            // or instead with hash

            x.ByExternallyManagedHashPartitions();
        });

});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Partitioning/partitioning_documents_on_duplicate_fields.cs#L35-L86' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring_partitioning_by_document_member' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Time-based Retention with Date Range Partitioning

A common use case is a high volume, append-only document table — think metrics, telemetry, or audit
samples — where retention is time based. By range-partitioning the table on a duplicated `DateTime` or
`DateTimeOffset` member, you turn "delete everything older than N months" into an instant
`DROP TABLE partition` instead of a large `DELETE` that bloats the table and forces vacuum churn.

Declare the monthly (or daily, weekly, etc.) partitions up front and let Marten manage them:

<!-- snippet: sample_partitioning_document_by_date_range -->
<a id='snippet-sample_partitioning_document_by_date_range'></a>
```cs
opts.Schema.For<MetricsSample>()
    .Duplicate(x => x.BucketEnd)
    .PartitionOn(x => x.BucketEnd, x =>
    {
        x.ByRange()
            .AddRange("2026_01",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero))
            .AddRange("2026_02",
                new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
    });
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Partitioning/Bug_4779_partition_document_by_date.cs#L53-L68' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_partitioning_document_by_date_range' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
Because PostgreSQL renders `timestamptz` partition bounds in the session time zone, always express your
range boundaries as explicit instants (use `DateTimeOffset` values, or UTC `DateTime` values). Marten and
Weasel compare the declared bounds against the database by instant, so the partitions stay stable across
deployments and across servers configured with different time zones.
:::

#### Rolling Time Windows <Badge type="tip" text="9.22" />

Declaring every partition up front only works while the set of partitions is fixed. Real time-series
storage needs the partition set to *move*: provision next month, drop last year. Rather than hand-writing
that DDL, describe the window and let Marten own it:

<!-- snippet: sample_partitioning_document_by_rolling_range -->
<a id='snippet-sample_partitioning_document_by_rolling_range'></a>
```cs
opts.Schema.For<MetricsSample>()
    .Duplicate(x => x.BucketEnd)
    // Keep 12 months of history, provision 3 months ahead. Marten creates the partitions at the
    // leading edge and drops the aged ones at the trailing edge -- no application-authored DDL.
    .PartitionOn(x => x.BucketEnd,
        x => x.ByRollingRange(PartitionPeriod.Month, periodsAhead: 3, periodsBehind: 12));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Partitioning/rolling_range_partitioning.cs#L36-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_partitioning_document_by_rolling_range' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The window — periods retained behind, the current period, periods provisioned ahead — is a pure function
of the policy and the clock, which is what makes this safe: a window that has rolled forward differs from
the database by exactly one new partition at the leading edge and one aged partition at the trailing edge.
Marten's schema migration only ever *adds* the new one, so rolling forward never triggers a destructive
table rebuild the way a moved list of declared ranges would. Partitions are named `m202607`, `d20260730`,
`y2026` and so on after the period they cover, and a `DEFAULT` overflow partition is always created, so a
row written outside the provisioned window is stored rather than rejected.

`PartitionPeriod` supports `Hour`, `Day`, `Week`, `Month`, and `Year`.

Marten runs the maintenance pass — roll forward, then drop everything below the retention floor — at
startup, alongside the [schema changes it already applies](/schema/migrations):

```cs
builder.Services.AddMarten(opts =>
{
    // ... the ByRollingRange() configuration above
}).ApplyAllDatabaseChangesOnStartup();
```

Dropping an aged partition is a `DROP TABLE` of one child table. That is the whole point: retention reclaim
stays O(1) instead of being a mass `DELETE` that bloats the table and forces vacuum churn. Only partitions
the policy itself named are ever dropped, so a hand-created partition, or one left over from a different
period size, is left strictly alone.

If a process is long-lived enough to outrun the number of periods you provision ahead — an hourly window
especially — run the pass yourself on whatever cadence the period size demands:

<!-- snippet: sample_applying_rolling_partitions -->
<a id='snippet-sample_applying_rolling_partitions'></a>
```cs
// Roll every rolling-window table forward to its current window and drop the partitions that have
// aged past their retention floor. Idempotent, and safe to run from several nodes at once.
await store.Advanced.ApplyRollingPartitionsAsync(token);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Partitioning/rolling_range_partitioning.cs#L50-L56' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_applying_rolling_partitions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
`ApplyRollingPartitionsAsync()` deletes data by design: everything in the aged partitions goes with them.
Use `Advanced.RollPartitionsForwardAsync()` for the purely additive half if you want to provision without
making a retention decision, and `Advanced.DropAgedRollingPartitionsAsync()` for retention alone.
:::

#### Externally Managed Range Partitions

If something outside Marten genuinely owns the partitions — [pg_partman](https://github.com/pgpartman/pg_partman),
or a migration tool of your own — use the externally managed variant so Marten creates the partitioned
parent table but leaves the individual partitions alone:

<!-- snippet: sample_partitioning_document_by_date_externally_managed -->
<a id='snippet-sample_partitioning_document_by_date_externally_managed'></a>
```cs
opts.Schema.For<MetricsSample>()
    .Duplicate(x => x.BucketEnd)
    .PartitionOn(x => x.BucketEnd, x => x.ByExternallyManagedRangePartitions());
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Partitioning/Bug_4779_partition_document_by_date.cs#L76-L82' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_partitioning_document_by_date_externally_managed' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
