# Schema Migrations and Patches

::: tip
All of the schema migration functionality is surfaced through Marten's [command line support](/configuration/cli) and that is the Marten team's
recommended approach for using the schema migration functionality described in this page.
:::

::: tip
Marten's schema management is built on top of the [Weasel](https://weasel.jasperfx.net/) schema
library. The APIs in this section — `Table`, `Function`, `Sequence`, `ISchemaObject`,
`ApplyAllConfiguredChangesToDatabaseAsync`, and friends — are Weasel types surfaced through Marten.
See the [Weasel documentation](https://weasel.jasperfx.net/) for details on the underlying
migration engine, the diff model, and custom schema object authoring.
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

<!-- snippet: sample_autocreateschemaobjects -->
<a id='snippet-sample_autocreateschemaobjects'></a>
```cs
var store = DocumentStore.For(opts =>
{
    // Marten will create any new objects that are missing,
    // attempt to update tables if it can, but drop and replace
    // tables that it cannot patch. The only mode that can lose
    // a whole table's data.
    opts.AutoCreateSchemaObjects = AutoCreate.All;

    // Marten will create any new objects that are missing and
    // update tables in place where it can. It never drops a whole
    // table -- but the update delta DOES drop columns, indexes and
    // foreign keys that the configuration no longer declares. For
    // example, removing a [DuplicateField] drops that column and
    // the data in it.
    opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

    // Marten will create missing objects on demand, but
    // will not change any existing schema objects
    opts.AutoCreateSchemaObjects = AutoCreate.CreateOnly;

    // Marten will neither create nor update any schema object --
    // and will not validate one either. The lazy storage check
    // simply returns, so a missing or stale table surfaces later
    // as a raw PostgreSQL error ("42P01 relation does not exist").
    // Use db-assert / AssertDatabaseMatchesConfigurationAsync()
    // for the validation. Note that db-apply, "resources setup"
    // and ApplyAllConfiguredChangesToDatabaseAsync() deliberately
    // treat None as CreateOrUpdate and migrate anyway.
    opts.AutoCreateSchemaObjects = AutoCreate.None;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/StoreOptionsTests.cs#L58-L93' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_autocreateschemaobjects' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As long as you're using a permissive auto creation mode (i.e., not _None_), you should be able to code in your application model
and let Marten change your development database as needed behind the scenes to match the active configuration.

::: warning
Two things about these modes are commonly misread, and both matter for choosing a production setting:

* **`CreateOrUpdate` is not "no data loss".** It never drops a whole table, but the update delta drops columns, indexes and
  foreign keys that the configuration no longer declares on a table it does know about. Removing a `[DuplicateField]` under
  `CreateOrUpdate` drops that column and the data in it.
* **`None` does not validate anything.** The lazy `EnsureStorageExistsAsync()` path returns without touching the database, so a
  missing or stale table surfaces later as a raw PostgreSQL error (`42P01 relation does not exist`) rather than as a Marten
  message about configuration drift. Validation is a separate, explicit call: `AssertDatabaseMatchesConfigurationAsync()` or the
  `db-assert` command. And `None` does not block the _apply_ commands either —
  `ApplyAllConfiguredChangesToDatabaseAsync()`, `db-apply` and `resources setup` deliberately treat `None` as `CreateOrUpdate`
  and migrate anyway, on the reasoning that you only run those when you mean to.
:::

::: tip
`AutoCreateSchemaObjects` defaults to `CreateOrUpdate`. If you never set it, Marten falls back to
`JasperFxOptions.ActiveProfile.ResourceAutoCreate` — whose **Production** profile also defaults to `CreateOrUpdate`. Nothing
switches your store to `None` in production unless your application does it explicitly.
:::

:::tip
In all of the usages shown below, the database migration functionality is able to function across the databases in a
[multi-tenancy by separate databases strategy](/configuration/multitenancy).
:::

## Exporting Database Migrations

It's somewhat unlikely that any self-respecting DBA is going to allow your application to have rights to execute schema changes programmatically,
so we're stuck needing some kind of migration strategy as we add document types, Javascript transformations, and retrofit indexes. Fortunately, we've got
a strong facility to detect and generate database migration scripts.

In usage, you would first need to tell Marten about every possible document type and any event store usage so that Marten
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

<!-- snippet: sample_writepatch -->
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
2. Document storage tables, "upsert" functions, and any configured indexes -- including missing columns or column type changes
3. Javascript transforms
4. The Hilo support table
5. The Event Store schema objects

### Include in your ci/cd pipeline

While there are many options to include these exported scripts in your ci/cd pipeline, we have an example using [grate](https://grate-devs.github.io/grate/) on the [DevOps page](/devops/devops).

## Apply All Outstanding Changes Upfront

To programmatically apply all detectable schema changes upfront , you can use this mechanism:

<!-- snippet: sample_applyallconfiguredchangestodatabase -->
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

<!-- snippet: sample_using_applyalldatabasechangesonstartup -->
<a id='snippet-sample_using_applyalldatabasechangesonstartup'></a>
```cs
// The normal Marten configuration
services.AddMarten(opts =>
    {
        // This helps isolate a test, not something you need to do
        // in normal usage
        opts.ApplyChangesLockId += 18;

        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = "apply_changes";
        opts.RegisterDocumentType<User>();
    })

    // Direct the application to apply all outstanding
    // database changes on application startup
    .ApplyAllDatabaseChangesOnStartup();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/bootstrapping_with_service_collection_extensions.cs#L154-L172' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_applyalldatabasechangesonstartup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In the option above, Marten is calling the same functionality within an `IHostedService` background task.

## Assert that a Schema Matches the Configuration

As a possible [environment test](http://codebetter.com/jeremymiller/2006/04/06/environment-tests-and-self-diagnosing-configuration-with-structuremap/), Marten can do a complete check of its known configuration versus the active Postgresql database and assert any differences
by throwing an exception:

<!-- snippet: sample_assertdatabasematchesconfiguration -->
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
