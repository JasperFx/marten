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

<!-- snippet: sample_using_DatabaseSchemaName_attribute -->
<a id='snippet-sample_using_databaseschemaname_attribute'></a>
```cs
[DatabaseSchemaName("organization")]
public class Customer
{
    [Identity] public string Name { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/DocumentMappingTests.cs#L866-L874' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_databaseschemaname_attribute' title='Start of snippet'>anchor</a></sup>
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

    // Set up table partitioning for the User document type
    opts.Schema.For<User>()
        .PartitionOn(x => x.Age, x =>
        {
            x.ByRange()
                .AddRange("young", 0, 20)
                .AddRange("twenties", 21, 29)
                .AddRange("thirties", 31, 39);
        });

    // Or use pg_partman to manage partitioning outside of Marten
    opts.Schema.For<User>()
        .PartitionOn(x => x.Age, x =>
        {
            x.ByExternallyManagedRangePartitions();

            // or instead with list

            x.ByExternallyManagedListPartitions();
        });

    // Or use PostgreSQL HASH partitioning and split the users over multiple tables
    opts.Schema.For<User>()
        .PartitionOn(x => x.UserName, x =>
        {
            x.ByHash("one", "two", "three");
        });

    opts.Schema.For<Issue>()
        .PartitionOn(x => x.Status, x =>
        {
            // There is a default partition for anything that doesn't fall into
            // these specific values
            x.ByList()
                .AddPartition("completed", "Completed")
                .AddPartition("new", "New");
        });

});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Partitioning/partitioning_documents_on_duplicate_fields.cs#L35-L81' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring_partitioning_by_document_member' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
