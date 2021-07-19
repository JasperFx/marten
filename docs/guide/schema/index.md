# Marten and the PostgreSQL Schema

Marten works by adding tables and functions (yes, Virginia, we've let stored procedures creep back into our life) to a PostgreSQL schema. Marten will generate and add a table and matching `upsert` function for each unique document type as needed. It also adds some other tables and functions for the [event store functionality](/guide/events/) and [HiLo id generation](/guide/documents/identity/sequential)

In all cases, the Marten schema objects are all prefixed with `mt_.`

As of Marten v0.8, you have much finer grained ability to control the automatic generation or updates of schema objects through the
`StoreOptions.AutoCreateSchemaObjects` like so:

<!-- snippet: sample_AutoCreateSchemaObjects -->
<a id='snippet-sample_autocreateschemaobjects'></a>
```cs
var store = DocumentStore.For(_ =>
{
    // Marten will create any new objects that are missing,
    // attempt to update tables if it can, but drop and replace
    // tables that it cannot patch.
    _.AutoCreateSchemaObjects = AutoCreate.All;

    // Marten will create any new objects that are missing or
    // attempt to update tables if it can. Will *never* drop
    // any existing objects, so no data loss
    _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

    // Marten will create missing objects on demand, but
    // will not change any existing schema objects
    _.AutoCreateSchemaObjects = AutoCreate.CreateOnly;

    // Marten will not create or update any schema objects
    // and throws an exception in the case of a schema object
    // not reflecting the Marten configuration
    _.AutoCreateSchemaObjects = AutoCreate.None;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/auto_create_mode_Tests.cs#L15-L39' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_autocreateschemaobjects' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To prevent unnecessary loss of data, even in development, on the first usage of a document type, Marten will:

1. Compare the current schema table to what's configured for that document type
1. If the table matches, do nothing
1. If the table is missing, try to create the table depending on the auto create schema setting shown above
1. If the table has new, searchable columns, adds the new column and runs an "UPDATE" command to duplicate the
   information in the JsonB data field. Do note that this could be expensive for large tables. This is also impacted
   by the auto create schema mode shown above.

Our thought is that in development you probably run in the "All" mode, but in production use one of the more restrictive auto creation modes.

**As of Marten v0.9.2, Marten will also check if the existing _upsert_ function and any table indexes match
what is configured in the document store, and attempts to update these objects if necessary based on the same
All/None/CreateOnly/CreateOrUpdate rules as the table storage.**


## Overriding Schema Name

By default marten will use the default `public` database scheme to create the document tables and function. You may, however, choose to set a different document store database schema name, like so:

<!-- snippet: sample_override_schema_name -->
<a id='snippet-sample_override_schema_name'></a>
```cs
_.DatabaseSchemaName = "other";
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/DocumentSchemaTests.cs#L496-L500' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_override_schema_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `Hilo` sequence table is always created in this document store database schema.

If you wish to assign certain document tables to different (new or existing) schemas, you can do so like that:

<!-- snippet: sample_override_schema_per_table -->
<a id='snippet-sample_override_schema_per_table'></a>
```cs
StoreOptions(_ =>
{
    _.Storage.MappingFor(typeof(User)).DatabaseSchemaName = "other";
    _.Storage.MappingFor(typeof(Issue)).DatabaseSchemaName = "overriden";
    _.Storage.MappingFor(typeof(Company));
    _.Storage.MappingFor(typeof(IntDoc));

    // this will tell marten to use the default 'public' schema name.
    _.DatabaseSchemaName = SchemaConstants.DefaultSchema;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/DocumentSchemaTests.cs#L335-L348' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_override_schema_per_table' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This will create the following tables in your database: `other.mt_doc_user`, `overriden.mt_doc_issue` and `public.mt_doc_company`. When a schema doesn't exist it will be generated in the database.

### Event Store
The EventStore database object are by default created in the document store DatabaseSchemaName. This can be overridden by setting the DatabaseSchemaName property of the event store options.

<!-- snippet: sample_override_schema_name_event_store -->
<a id='snippet-sample_override_schema_name_event_store'></a>
```cs
_.Events.DatabaseSchemaName = "event_store";
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/using_the_schema_objects_Tests.cs#L60-L62' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_override_schema_name_event_store' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This will ensure that all EventStore tables (mt_stream, mt_events, ...) and functions (mt_apply_transform, mt_apply_aggregation, ...) are created in the `event_store` schema.
