# Event Store Configuration

## Specifying the Schema

The database schema name for the event store tables is by default, the same schema as the document store
itself. The event storage can be explicitly moved to a separate schema as shown below:

<!-- snippet: sample_set_event_store_schema -->
<a id='snippet-sample_set_event_store_schema'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    opts.Events.DatabaseSchemaName = "events";
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/StartStreamSamples.cs#L13-L22' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_set_event_store_schema' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Stream Identity

The Event Store in Marten can identify and index streams either as Guids
(`System.Guid`) or strings (`System.String`). This is reflected in the overloads
of `IEventStore` such as `IEventStore.StartStream`, `IEventStore.Append` and `IEventStore.AggregateStream`
that accept either `string` or `Guid` as the stream identifier.

Configuration of the stream identity is done through `StoreOptions.Events.StreamIdentity`. If not set, Marten defaults to `StreamIdentity.AsGuid`.
The identity is configured once per store, whereby different stream identity types cannot be mixed. The following sample
demonstrates configuring streams to be identified as strings.

<!-- snippet: sample_setting_stream_identity -->
<a id='snippet-sample_setting_stream_identity'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Override the stream identity to use strings
    opts.Events.StreamIdentity = StreamIdentity.AsString;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/StartStreamSamples.cs#L27-L37' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_setting_stream_identity' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Stream identity effects the underlying database schema of the Event Store related tables. Namely, using string identities configures `stream_id` in the `mt_events` table to be `varchar`, whereas `uuid` would be used for GUIDs. The same applies to the `id` column in `mt_streams` table.

## Multi-Tenancy

The event storage can opt into conjoined multi-tenancy with this syntax:

<!-- snippet: sample_making_the_events_multi_tenanted -->
<a id='snippet-sample_making_the_events_multi_tenanted'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // And that's all it takes, the events are now multi-tenanted
    opts.Events.TenancyStyle = TenancyStyle.Conjoined;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L235-L245' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_making_the_events_multi_tenanted' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Big Integer Event Sequences <Badge type="tip" text="8.x" />

By default, Marten's internal PostgreSQL functions (`mt_quick_append_events`) use standard 32-bit `int` types for
event version numbers, sequence values, and return types. This works well for the vast majority of systems, but
the `int` type has a maximum value of approximately 2.1 billion. For very high-volume event stores that may
exceed this threshold, Marten provides an opt-in flag to switch these functions to use 64-bit `bigint` types instead:

```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Opt into bigint (64-bit) types for event sequences and versions
    // in the PostgreSQL event append functions
    opts.Events.EnableBigIntEvents = true;
});
```

When `EnableBigIntEvents` is `true`, the generated `mt_quick_append_events` function will declare its
`event_version`, `seq`, and `return_value` variables as `bigint` instead of `int`, and return `bigint[]`
instead of `int[]`. This prevents integer overflow errors when the global event sequence counter surpasses
the ~2.1 billion limit of a 32-bit integer.

::: warning
Enabling this flag will cause Marten to regenerate the `mt_quick_append_events` function with different
type signatures. This means a schema migration will be required when you first enable the flag. Marten's
normal schema migration tooling will handle this automatically.
:::

::: info
The `EnableBigIntEvents` flag is `false` by default in Marten 8.x for backward compatibility. Starting
in **Marten 9.0**, this flag will default to `true`.
:::
