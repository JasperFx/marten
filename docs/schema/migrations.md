# Schema Migrations and Patches

::: tip
All of the schema migration functionality is surfaced through Marten's [command line support](/configuration/cli) and that is the Marten team's
recommended approach for using the schema migration functionality described in this page.
:::

While it's going to be far less mechanical work than persisting an application via relational tables, Marten still needs to create
matching schema objects in your Postgresql database and you'll need some mechanism for keeping your database schema up to date
with the Marten `StoreOptions` configuration in your system.

## Development Time with "Auto Create" Mode

::: warning
Heads up, all the API methods for invoking schema checks or patches or migrations are now asynchronous as of Marten V4.
:::

As long as you have rights to alter your Postgresql database, you can happily set up Marten in one of the permissive "AutoCreate"
modes and not worry about schema changes at all as you happily code new features and change existing document types:

<!-- snippet: sample_AutoCreateSchemaObjects -->
<a id='snippet-sample_autocreateschemaobjects'></a>
```cs
var store = DocumentStore.For(opts =>
{
    // Marten will create any new objects that are missing,
    // attempt to update tables if it can, but drop and replace
    // tables that it cannot patch.
    opts.AutoCreateSchemaObjects = AutoCreate.All;

    // Marten will create any new objects that are missing or
    // attempt to update tables if it can. Will *never* drop
    // any existing objects, so no data loss
    opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

    // Marten will create missing objects on demand, but
    // will not change any existing schema objects
    opts.AutoCreateSchemaObjects = AutoCreate.CreateOnly;

    // Marten will not create or update any schema objects
    // and throws an exception in the case of a schema object
    // not reflecting the Marten configuration
    opts.AutoCreateSchemaObjects = AutoCreate.None;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/StoreOptionsTests.cs#L39-L63' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_autocreateschemaobjects' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As long as you're using a permissive auto creation mode (i.e., not _None_), you should be able to code in your application model
and let Marten change your development database as needed behind the scenes to match the active configuration.

:::tip
In all of the usages shown below, the database migration functionality is able to function across the databases in a
[multi-tenancy by separate databases strategy](/configuration/multitenancy).
:::

## Exporting Database Migrations

It's somewhat unlikely that any self-respecting DBA is going to allow your application to have rights to execute schema changes programmatically,
so we're stuck needing some kind of migration strategy as we add document types, Javascript transformations, and retrofit indexes. Fortunately, we've got
a strong facility to detect and generate database migration scripts.

In usage, you would first need to tell Marten about every possible document type, any event store usage, and any
[javascript transforms](/documents/plv8) so that Marten
"knows" how to make the full comparison:

<!-- snippet: sample_configure-document-types-upfront -->
<a id='snippet-sample_configure-document-types-upfront'></a>
```cs
using var store = DocumentStore.For(_ =>
{
    // This is enough to tell Marten that the User
    // document is persisted and needs schema objects
    _.Schema.For<User>();

    // Lets Marten know that the event store is active
    _.Events.AddEventType(typeof(MembersJoined));
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/MigrationSamples.cs#L11-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure-document-types-upfront' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The easiest possible way to export SQL migration files is to use Marten's [command line tooling](/configuration/cli) with the single command:

```bash
dotnet run -- db-patch [filename]
```

or for backward compatibility with Marten <V5.0

```bash
dotnet run -- marten-patch [filename]
```

If you'd rather write a database SQL migration file with your own code, bootstrap your `IDocumentStore` pointing to the database connection you
want to update, and use:

<!-- snippet: sample_WritePatch -->
<a id='snippet-sample_writepatch'></a>
```cs
// All migration code is async now!
await store.Storage.Database.WriteMigrationFileAsync("1.initial.sql");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MigrationSamples.cs#L19-L23' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_writepatch' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The command above will generate a file called "1.initial.sql" to update the schema, and a second file called
"1.initial.drop.sql" that attempts to rollback all of the changes from "1.initial.sql." Today, the migration
mechanism covers:

1. Creates any missing database schemas
1. Document storage tables, "upsert" functions, and any configured indexes -- including missing columns or column type changes
1. Javascript transforms
1. The Hilo support table
1. The Event Store schema objects

## Apply All Outstanding Changes Upfront

To programmatically apply all detectable schema changes upfront , you can use this mechanism:

<!-- snippet: sample_ApplyAllConfiguredChangesToDatabase -->
<a id='snippet-sample_applyallconfiguredchangestodatabase'></a>
```cs
await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MigrationSamples.cs#L25-L27' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_applyallconfiguredchangestodatabase' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

With the [command line tooling](/configuration/cli), it's:

```bash
dotnet run -- db-apply
```

or in Marten <V5.0:

```bash
dotnet run -- marten-apply
```

Lastly, Marten V5 adds a new option to have the latest database changes detected and applied on application startup with

<!-- snippet: sample_using_ApplyAllDatabaseChangesOnStartup -->
<a id='snippet-sample_using_applyalldatabasechangesonstartup'></a>
```cs
// The normal Marten configuration
services.AddMarten(opts =>
    {
        opts.Connection(ConnectionSource.ConnectionString);
        opts.RegisterDocumentType<User>();
    })

    // Direct the application to apply all outstanding
    // database changes on application startup
    .ApplyAllDatabaseChangesOnStartup();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/MartenServiceCollectionExtensionsTests.cs#L165-L178' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_applyalldatabasechangesonstartup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In the option above, Marten is calling the same functionality within an `IHostedService` background task.

## Assert that a Schema Matches the Configuration

As a possible [environment test](http://codebetter.com/jeremymiller/2006/04/06/environment-tests-and-self-diagnosing-configuration-with-structuremap/), Marten can do a complete check of its known configuration versus the active Postgresql database and assert any differences
by throwing an exception:

<!-- snippet: sample_AssertDatabaseMatchesConfiguration -->
<a id='snippet-sample_assertdatabasematchesconfiguration'></a>
```cs
await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MigrationSamples.cs#L29-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_assertdatabasematchesconfiguration' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The exception will list out all the DDL changes that are missing.

With the [command line tooling](/configuration/cli), it's:

```bash
dotnet run -- db-assert
```

or

```bash
dotnet run -- marten-assert
```
