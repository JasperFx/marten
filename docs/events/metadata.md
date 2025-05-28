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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L118-L131' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configureeventmetadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
The basic [IEvent](https://github.com/JasperFx/jasperfx/blob/main/src/JasperFx.Events/Event.cs#L34-L176) abstraction and quite a bit of other generic
event sourcing code moved in Marten 8.0 to the shared JasperFx.Events library.
:::

The actual metadata is accessible from the `IEvent` interface event wrappers as shown below (which are implemented by internal `Event<T>`):

```cs
public interface IEvent
{
    /// <summary>
    ///     Unique identifier for the event. Uses a sequential Guid
    /// </summary>
    Guid Id { get; set; }

    /// <summary>
    ///     The version of the stream this event reflects. The place in the stream.
    /// </summary>
    long Version { get; set; }

    /// <summary>
    ///     The sequential order of this event in the entire event store
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
    ///     The .Net type of the event body
    /// </summary>
    Type EventType { get; }

    /// <summary>
    ///     Marten's type alias string for the Event type
    /// </summary>
    string EventTypeName { get; set; }

    /// <summary>
    ///     Marten's string representation of the event type
    ///     in assembly qualified name
    /// </summary>
    string DotNetTypeName { get; set; }

    /// <summary>
    ///     Optional metadata describing the causation id
    /// </summary>
    string? CausationId { get; set; }

    /// <summary>
    ///     Optional metadata describing the correlation id
    /// </summary>
    string? CorrelationId { get; set; }

    /// <summary>
    ///     Optional user defined metadata values. This may be null.
    /// </summary>
    Dictionary<string, object>? Headers { get; set; }

    /// <summary>
    ///     Has this event been archived and no longer applicable
    ///     to projected views
    /// </summary>
    bool IsArchived { get; set; }

    /// <summary>
    ///     Marten's name for the aggregate type that will be persisted
    ///     to the streams table. This will only be available when running
    ///     within the Async Daemon
    /// </summary>
    public string? AggregateTypeName { get; set; }

    /// <summary>
    ///     Set an optional user defined metadata value by key
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    void SetHeader(string key, object value);

    /// <summary>
    ///     Get an optional user defined metadata value by key
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    object? GetHeader(string key);
}
```
