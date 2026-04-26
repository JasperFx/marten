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

### Enums, Nullable Values, and Registered Value Types <Badge type="tip" text="8.x" />

`FlatTableProjection.Map(...)` honors three things on top of the property type
itself:

1. **Enum columns** follow `StoreOptions.Advanced.DuplicatedFieldEnumStorage`,
   which defaults to your serializer's `EnumStorage`. Set it explicitly when
   you want to lock the column type:

   ```cs
   var store = DocumentStore.For(opts =>
   {
       opts.Connection(connectionString);
       // Store all duplicated-field-style enums (including FlatTableProjection
       // columns) as text. The matching table column should be `varchar`/`text`.
       opts.Advanced.DuplicatedFieldEnumStorage = EnumStorage.AsString;

       opts.Projections.Add<MyFlatProjection>(ProjectionLifecycle.Inline);
   });
   ```

   With `EnumStorage.AsString`, `Map(x => x.Status)` writes `Status.ToString()`
   into a text column. With `EnumStorage.AsInteger` (the default for the
   built-in JSON serializers), the same call writes the underlying integer
   value into an int column. Make sure the column type you declare on
   `Table.AddColumn<T>(...)` matches your `DuplicatedFieldEnumStorage` setting.

2. **Nullable enums and nullable value-typed properties** are handled
   automatically — when the property value is `null`, Marten writes `DBNull`;
   otherwise the same enum / value-type projection rules apply. The target
   column needs to be marked `AllowNulls()` (or otherwise be nullable).

3. **Registered value types** (e.g. Vogen-style single-value wrappers
   registered through `opts.RegisterValueType<TWrapper>()`) are unwrapped to
   their inner primitive automatically. `Map(x => x.MyValueObject)` projects
   the value type's inner property into the column without any manual
   `cfg.Map(x => x.MyValueObject.Value, "...")` workaround. The column type
   should match the wrapped primitive (e.g. `Table.AddColumn<int>(...)` for a
   `record struct WrapperId(int Value)`).

::: warning
Prior to Marten 8.x, `FlatTableProjection` ignored
`DuplicatedFieldEnumStorage` and could not handle registered value types or
nullable wrappers. Mapping a `string` enum column threw
`Writing values of '<EnumType>' is not supported for parameters having
NpgsqlDbType 'Integer'`, and mapping a registered value-type property threw
`Can't infer NpgsqlDbType for type <Wrapper>`. See
[#4290](https://github.com/JasperFx/marten/issues/4290) and
[#4291](https://github.com/JasperFx/marten/issues/4291).
:::

### Partial-Mapping Events (Update-Only) <Badge type="tip" text="8.x" />

When an event mapped into a `FlatTableProjection` does not populate every non-primary-key
column on the target table, Marten generates an **UPDATE-only** function for that event:

```sql
-- For an event that maps only the `field` column:
CREATE FUNCTION mt_upsert_proj_eventb(p_id uuid, p_field text) RETURNS void
LANGUAGE plpgsql AS $function$
BEGIN
  UPDATE proj SET field = p_field WHERE id = p_id;
END;
$function$;
```

Events that map **every** non-PK column still use the original `INSERT … ON CONFLICT DO UPDATE`
form so they can both create and update rows.

This means partial-mapping events are **safe against NOT NULL constraints** on columns they
don't populate — they cannot create a half-populated row. It also means that if a partial
event fires for a stream whose row does not yet exist, the UPDATE matches zero rows and is
a no-op. Streams should therefore start with a full-mapping event that can create the row.

::: warning
Prior to Marten 8.x, all events generated `INSERT … ON CONFLICT DO UPDATE`. If your table
had NOT NULL columns not populated by every event, appending those events would raise
`23502: null value in column "…" violates not-null constraint`. The partial-mapping
UPDATE-only behavior resolves this.
:::

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/Flattened/using_event_projection_for_flat_tables.cs#L29-L80' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_import_sql_event_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A couple notes about the `EventProjection` approach:

* **Schema management** -- The `Table` model comes from the [Weasel](https://weasel.jasperfx.net/) library. Adding it
  to `SchemaObjects` allows Marten's built-in schema management to create and migrate the table automatically. See the
  [Weasel documentation](https://weasel.jasperfx.net/) for the full schema-object model.
* **Batched execution** -- The `QueueSqlCommand()` method doesn't execute inline. Instead, it adds the SQL to be executed
  in a batch query when you call `IDocumentSession.SaveChangesAsync()`. This batching reduces network round trips to the
  database and is a consistent performance win.
* **Event metadata access** -- The `Project()` methods use `IEvent<T>` envelope types, giving you access to event metadata
  like timestamps, version information, and stream identity. This is something the declarative `FlatTableProjection`
  cannot currently provide.
* **Full SQL control** -- You can write any SQL you need: inserts, updates, deletes, or even complex statements with
  subqueries. This is useful when your projection logic doesn't fit the `Map`/`Increment`/`SetValue` patterns of
  `FlatTableProjection`.
