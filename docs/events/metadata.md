# Event Metadata

See [Marten Metadata](/documents/metadata) for more information and examples
about capturing metadata as part of `IDocumentSession` unit of work operations.

The metadata tracking for events can be extended in Marten by opting into extra fields
for causation, correlation, and key/value headers with this syntax as part of configuring
Marten:

<!-- snippet: sample_ConfigureEventMetadata -->
<a id='snippet-sample_configureeventmetadata'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("connection string");

    // This adds additional metadata tracking to the
    // event store tables
    opts.Events.MetadataConfig.HeadersEnabled = true;
    opts.Events.MetadataConfig.CausationIdEnabled = true;
    opts.Events.MetadataConfig.CorrelationIdEnabled = true;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L114-L127' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configureeventmetadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The actual metadata is accessible from the `IEvent` interface or `Event<T>` event wrappers as shown below:

<!-- snippet: sample_IEvent -->
<a id='snippet-sample_ievent'></a>
```cs
public interface IEvent : IEventMetadata
{
    /// <summary>
    /// Unique identifier for the event. Uses a sequential Guid
    /// </summary>
    Guid Id { get; set; }

    /// <summary>
    /// The version of the stream this event reflects. The place in the stream.
    /// </summary>
    long Version { get; set; }

    /// <summary>
    /// The sequential order of this event in the entire event store
    /// </summary>
    long Sequence { get; set; }

    /// <summary>
    ///     The actual event data body
    /// </summary>
    object Data { get; }

    /// <summary>
    ///     If using Guid's for the stream identity, this will
    ///     refer to the Stream's Id, otherwise it will always be Guid.Empty
    /// </summary>
    Guid StreamId { get; set; }

    /// <summary>
    ///     If using strings as the stream identifier, this will refer
    ///     to the containing Stream's Id
    /// </summary>
    string? StreamKey { get; set; }

    /// <summary>
    ///     The UTC time that this event was originally captured
    /// </summary>
    DateTimeOffset Timestamp { get; set; }

    /// <summary>
    ///     If using multi-tenancy by tenant id
    /// </summary>
    string TenantId { get; set; }

    /// <summary>
    /// The .Net type of the event body
    /// </summary>
    Type EventType { get; }

    /// <summary>
    /// Marten's type alias string for the Event type
    /// </summary>
    string EventTypeName { get; set; }

    /// <summary>
    /// Marten's string representation of the event type
    /// in assembly qualified name
    /// </summary>
    string DotNetTypeName { get; set; }

    /// <summary>
    /// Has this event been archived and no longer applicable
    /// to projected views
    /// </summary>
    bool IsArchived { get; set; }
}

public interface IEventMetadata
{
    /// <summary>
    /// Optional metadata describing the causation id
    /// </summary>
    string? CausationId { get; set; }

    /// <summary>
    /// Optional metadata describing the correlation id
    /// </summary>
    string? CorrelationId { get; set; }

    /// <summary>
    /// Optional user defined metadata values. This may be null.
    /// </summary>
    Dictionary<string, object>? Headers { get; set; }

    /// <summary>
    /// Set an optional user defined metadata value by key
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    void SetHeader(string key, object value);

    /// <summary>
    /// Get an optional user defined metadata value by key
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    object? GetHeader(string key);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Events/Event.cs#L8-L108' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ievent' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Overrides

It is possible to apply or override metadata for individual events within a session. Note that when the events are saved, any metadata values set for the session are applied first, with individual event metadata overrides applied after.

Use the following commands to apply metadata to specific events:

<!-- snippet: sample_event_metadata_overrides -->
<a id='snippet-sample_event_metadata_overrides'></a>
```cs
[Fact]
public async Task check_event_metadata_overrides()
{
    StoreOptions(_ => _.Events.MetadataConfig.EnableAll());

    const string correlationId = "test-correlation-id";
    theSession.CorrelationId = correlationId;

    const string causationId = "test-causation-id";
    theSession.CausationId = causationId;

    const string userDefinedMetadata1Name = "my-header-1";
    const string userDefinedMetadata1Value = "my-header-1-value";
    theSession.SetHeader(userDefinedMetadata1Name, userDefinedMetadata1Value);
    const string userDefinedMetadata2Name = "my-header-2";
    const string userDefinedMetadata2Value = "my-header-2-value";
    theSession.SetHeader(userDefinedMetadata2Name, userDefinedMetadata2Value);

    // override the correlation ids
    const string correlationIdOverride = "override-correlation-id";
    theSession.Events.ApplyCorrelationId(correlationIdOverride, started, joined);

    // override the causation ids
    const string causationIdOverride = "override-causation-id";
    theSession.Events.ApplyCausationId(causationIdOverride, started, joined);

    // update an existing header on one event
    const string overrideMetadata1Value = "my-header-1-override-value";
    theSession.Events.ApplyHeader(userDefinedMetadata1Name, overrideMetadata1Value, started);

    // add a new header on one event
    const string overrideMetadata3Name = "my-header-override";
    const string overrideMetadata3Value = "my-header-override-value";
    theSession.Events.ApplyHeader(overrideMetadata3Name, overrideMetadata3Value, slayed);

    // actually add the events to the session
    // this can be done before or after metadata overrides are applied
    var streamId = theSession.Events
        .StartStream<QuestParty>(started, joined, slayed).Id;
    await theSession.SaveChangesAsync();

    var events = await theSession.Events.FetchStreamAsync(streamId);

    events[0].CorrelationId.ShouldBe(correlationIdOverride);
    events[1].CorrelationId.ShouldBe(correlationIdOverride);
    events[2].CorrelationId.ShouldBe(correlationId);

    events[0].CausationId.ShouldBe(causationIdOverride);
    events[1].CausationId.ShouldBe(causationIdOverride);
    events[2].CausationId.ShouldBe(causationId);

    events[0].GetHeader(userDefinedMetadata1Name).ToString().ShouldBe(overrideMetadata1Value);
    events[0].GetHeader(userDefinedMetadata2Name).ToString().ShouldBe(userDefinedMetadata2Value);
    events[0].GetHeader(overrideMetadata3Name).ShouldBeNull();

    events[1].GetHeader(userDefinedMetadata1Name).ToString().ShouldBe(userDefinedMetadata1Value);
    events[1].GetHeader(userDefinedMetadata2Name).ToString().ShouldBe(userDefinedMetadata2Value);
    events[1].GetHeader(overrideMetadata3Name).ShouldBeNull();

    events[2].GetHeader(userDefinedMetadata1Name).ToString().ShouldBe(userDefinedMetadata1Value);
    events[2].GetHeader(userDefinedMetadata2Name).ToString().ShouldBe(userDefinedMetadata2Value);
    events[2].GetHeader(overrideMetadata3Name).ToString().ShouldBe(overrideMetadata3Value);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/flexible_event_metadata.cs#L216-L283' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_event_metadata_overrides' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
