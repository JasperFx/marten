# Stream Identity

The Event Store in Marten can identify and index streams either as Guids (`System.Guid`) or strings (`System.String`). This is reflected in the overloads of `IEventStore` such as `IEventStore.StartStream`, `IEventStore.Append` and `IEventStore.AggregateStream` that accept either `string` or `Guid` as the stream identifier.

## Configuring Event Stream Identity

Configuration of the stream identity is done through `StoreOptions.Events.StreamIdentity`. If not set, Marten defaults to `StreamIdentity.AsGuid`. The identity is configured once per store, whereby different stream identity types cannot be mixed. The following sample demonstrates configuring streams to be identified as strings.

<!-- snippet: sample_eventstore-configure-stream-identity -->
<a id='snippet-sample_eventstore-configure-stream-identity'></a>
```cs
storeOptions.Events.StreamIdentity = StreamIdentity.AsString;
storeOptions.Projections.SelfAggregate<QuestPartyWithStringIdentifier>(ProjectionLifecycle.Async);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/event_store_with_string_identifiers_for_stream.cs#L19-L22' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_eventstore-configure-stream-identity' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

# Practical Implications

Stream identity effects the underlying database schema of the Event Store related tables. Namely, using string identities configures `stream_id` in the `mt_events` table to be `varchar`, whereas `uuid` would be used for GUIDs. The same applies to the `id` column in `mt_streams` table.
