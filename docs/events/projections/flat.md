# Flat Table Projections

Marten has yet another projection recipe for writing event data to flat projections.

Let's dive right into a sample usage of this. If you're a software developer long enough and move around just a little bit,
you're going to get sucked into building a workflow for importing flat files of dubious quality from external partners or
customers. I'm going to claim that event sourcing is a good fit for this problem domain (and
also suggesting this pretty strongly at work). That being said, here's what the event types might look like that are
recording the progress of a file import:

<!-- snippet: sample_flat_table_events -->
<a id='snippet-sample_flat_table_events'></a>
```cs
public record ImportStarted(
    DateTimeOffset Started,
    string ActivityType,
    string CustomerId,
    int PlannedSteps);

public record ImportProgress(
    string StepName,
    int Records,
    int Invalids);

public record ImportFinished(DateTimeOffset Finished);

public record ImportFailed;
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/FlatTableProjection.cs#L14-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_flat_table_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

At some point, we're going to want to apply some metrics to the execution history to understand the average size of the
incoming files, what times of the day have more or less traffic, and performance information broken down by file size,
file type, and who knows what. This sounds to me like a perfect use case for SQL queries against a flat table.

## Using FlatTableProjection

Marten's `FlatTableProjection` provides a declarative, fluent API for projecting events to a flat table. This approach
handles column mapping, upsert generation, and schema management automatically:

<!-- snippet: sample_flat_import_projection -->
<a id='snippet-sample_flat_import_projection'></a>
```cs
public class FlatImportProjection: FlatTableProjection
{
    // I'm telling Marten to use the same database schema as the events from
    // the Marten configuration in this application
    public FlatImportProjection() : base("import_history", SchemaNameSource.EventSchema)
    {
        // We need to explicitly add a primary key
        Table.AddColumn<Guid>("id").AsPrimaryKey();

        Options.TeardownDataOnRebuild = true;

        Project<ImportStarted>(map =>
        {
            // Set values in the table from the event
            map.Map(x => x.ActivityType);
            map.Map(x => x.CustomerId);
            map.Map(x => x.PlannedSteps, "total_steps")
                .DefaultValue(0);

            map.Map(x => x.Started);

            // Initial values
            map.SetValue("status", "started");
            map.SetValue("step_number", 0);
            map.SetValue("records", 0);
        });

        Project<ImportProgress>(map =>
        {
            // Add 1 to this column when this event is encountered
            map.Increment("step_number");

            // Update a running sum of records progressed
            // by the number of records on this event
            map.Increment(x => x.Records);

            map.SetValue("status", "working");
        });

        Project<ImportFinished>(map =>
        {
            map.Map(x => x.Finished);
            map.SetValue("status", "completed");
        });

        // Just gonna delete the record of any failures
        Delete<ImportFailed>();

    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/FlatTableProjection.cs#L90-L143' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_flat_import_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A couple notes on this version of the code:

* `FlatTableProjection` is adding columns to its table based on the designated column mappings.
  You can happily customize the `FlatTableProjection.Table` object to add indexes, constraints, or defaults.
* Marten is able to apply schema migrations and manage the table from the `FlatTableProjection` as long as it's registered with Marten.
* When you call `Map(x => x.ActivityType)`, Marten is by default mapping that to a snake_cased derivation of the member
  name for the column, so "activity_type". You can explicitly map the column name yourself.
* The call to `Map(expression)` chains a fluent builder for the table column if you want to further customize the table
  column with default values or constraints like the `NotNull()`.
* In this case, I'm building a database row per event stream. The `FlatTableProjection` can also map to arbitrary
  members of each event type.
* The `Project<T>(lambda)` configuration leads to a runtime, code generation of a Postgresql upsert command so
  as to not be completely dependent upon events being captured in the exact right order. I think this will be more
  robust in real life usage than the first, more explicit version.

The `FlatTableProjection` in its first incarnation is not yet able to use event metadata.

## Using EventProjection for Flat Tables

::: tip
The `EventProjection` approach shown below is more explicit code than `FlatTableProjection`, but it is also
more flexible. Use `EventProjection` when you need full control over the SQL being generated, need to access
event metadata through the `IEvent<T>` envelope, or when the declarative `FlatTableProjection` API does not
support your use case. The tradeoff is that you are writing raw SQL yourself, so you are responsible for
getting the SQL correct and handling upsert logic on your own.
:::

As an alternative to the more rigid `FlatTableProjection` approach, you can use Marten's `EventProjection` as a
base class and write explicit SQL to project events into a flat table. This gives you complete control over the
SQL operations and full access to event metadata:

<!-- snippet: sample_import_sql_event_projection -->
<a id='snippet-sample_import_sql_event_projection'></a>
```cs
public partial class ImportSqlProjection: EventProjection
{
    public ImportSqlProjection()
    {
        // Define the table structure here so that
        // Marten can manage this for us in its schema
        // management
        var table = new Table("import_history");
        table.AddColumn<Guid>("id").AsPrimaryKey();
        table.AddColumn<string>("activity_type").NotNull();
        table.AddColumn<string>("customer_id").NotNull();
        table.AddColumn<DateTimeOffset>("started").NotNull();
        table.AddColumn<DateTimeOffset>("finished");

        SchemaObjects.Add(table);

        // Telling Marten to delete the table data as the
        // first step in rebuilding this projection
        Options.DeleteDataInTableOnTeardown(table.Identifier);
    }

    // Use the IEvent<T> envelope to access event metadata
    // like stream identity and timestamps
    public void Project(IEvent<ImportStarted> e, IDocumentOperations ops)
    {
        ops.QueueSqlCommand(
            "insert into import_history (id, activity_type, customer_id, started) values (?, ?, ?, ?)",
            e.StreamId, e.Data.ActivityType, e.Data.CustomerId, e.Data.Started
        );
    }

    public void Project(IEvent<ImportFinished> e, IDocumentOperations ops)
    {
        ops.QueueSqlCommand(
            "update import_history set finished = ? where id = ?",
            e.Data.Finished, e.StreamId
        );
    }

    // You can use any SQL operation, including deletes
    public void Project(IEvent<ImportFailed> e, IDocumentOperations ops)
    {
        ops.QueueSqlCommand(
            "delete from import_history where id = ?",
            e.StreamId
        );
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/Flattened/using_event_projection_for_flat_tables.cs#L18-L75' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_import_sql_event_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A couple notes about the `EventProjection` approach:

* **Schema management** -- The `Table` model comes from the Weasel library. Adding it to `SchemaObjects` allows Marten's
  built-in schema management to create and migrate the table automatically.
* **Batched execution** -- The `QueueSqlCommand()` method doesn't execute inline. Instead, it adds the SQL to be executed
  in a batch query when you call `IDocumentSession.SaveChangesAsync()`. This batching reduces network round trips to the
  database and is a consistent performance win.
* **Event metadata access** -- The `Project()` methods use `IEvent<T>` envelope types, giving you access to event metadata
  like timestamps, version information, and stream identity. This is something the declarative `FlatTableProjection`
  cannot currently provide.
* **Full SQL control** -- You can write any SQL you need: inserts, updates, deletes, or even complex statements with
  subqueries. This is useful when your projection logic doesn't fit the `Map`/`Increment`/`SetValue` patterns of
  `FlatTableProjection`.
