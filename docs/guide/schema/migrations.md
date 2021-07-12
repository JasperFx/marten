# Schema Migrations and Patches

While it's going to be far less mechanical work than persisting an application via relational tables, Marten still needs to create
matching schema objects in your Postgresql database and you'll need some mechanism for keeping your database schema up to date
with the Marten `StoreOptions` configuration in your system.

## Development Time with "Auto Create" Mode

As long as you have rights to alter your Postgresql database, you can happily set up Marten in one of the permissive "AutoCreate"
modes and not worry about schema changes at all as you happily code new features and change existing document types:

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

As long as you're using a permissive auto creation mode (i.e., not _None_), you should be able to code in your application model
and let Marten change your development database as needed behind the scenes to match the active configuration.

## Exporting Schema Patches

It's somewhat unlikely that any self-respecting DBA is going to allow your application to have rights to execute schema changes programmatically,
so we're stuck needing some kind of migration strategy as we add document types, Javascript transformations, and retrofit indexes.
Fortunately, we've got a decent start on doing just that with the `IDocumentStore.Schema.WritePatch(string file)` command that
will dump a SQL file with all the [Data Definition Language](https://en.wikipedia.org/wiki/Data_definition_language) (DDL) commands necessary
to bring a database schema into alignment with the current Marten configuration.

In usage, you would need to tell Marten about every possible document type, any event store usage, and any
[javascript transforms](/guide/documents/advanced/javascript-transformations) so that Marten
"knows" how to make the full comparison:

<!-- snippet: sample_configure-document-types-upfront -->
<a id='snippet-sample_configure-document-types-upfront'></a>
```cs
var store = DocumentStore.For(_ =>
{
    // This is enough to tell Marten that the User
    // document is persisted and needs schema objects
    _.Schema.For<User>();

    // Lets Marten know that the event store is active
    _.Events.AddEventType(typeof(MembersJoined));
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/WritePatch_smoke_tests.cs#L16-L26' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure-document-types-upfront' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Then to write a patch DDL file, bootstrap your `IDocumentStore` pointing to the database connection you
want to update, and use:

<!-- snippet: sample_WritePatch -->
<a id='snippet-sample_writepatch'></a>
```cs
await store.Schema.WriteMigrationFile("1.initial.sql");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/WritePatch_smoke_tests.cs#L28-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_writepatch' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The command above will generate a file called "1.initial.sql" to update the schema, and a second file called
"1.initial.drop.sql" that attempts to rollback all of the changes from "1.initial.sql." Today, the `WritePatch()`
mechanism covers:

1. Creates any missing database schemas
1. Document storage tables, "upsert" functions, and any configured indexes
1. Javascript transforms
1. The Hilo support table
1. The Event Store schema objects

## Apply All Outstanding Changes Upfront

To programmatically apply all detectable schema changes upfront when an application is first
bootstrapped, you can use this mechanism:

<!-- snippet: sample_ApplyAllConfiguredChangesToDatabase -->
<a id='snippet-sample_applyallconfiguredchangestodatabase'></a>
```cs
await store.Schema.ApplyAllConfiguredChangesToDatabase();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/WritePatch_smoke_tests.cs#L32-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_applyallconfiguredchangestodatabase' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Assert that a Schema Matches the Configuration

As a possible [environment test](http://codebetter.com/jeremymiller/2006/04/06/environment-tests-and-self-diagnosing-configuration-with-structuremap/), Marten can do a complete check of its known configuration versus the active Postgresql database and assert any differences
by throwing an exception:

<!-- snippet: sample_AssertDatabaseMatchesConfiguration -->
<a id='snippet-sample_assertdatabasematchesconfiguration'></a>
```cs
await store.Schema.AssertDatabaseMatchesConfiguration();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/WritePatch_smoke_tests.cs#L36-L38' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_assertdatabasematchesconfiguration' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The exception will list out all the DDL changes that are missing.
