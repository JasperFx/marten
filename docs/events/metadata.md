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

## Overriding Metadata <Badge type="tip" text="8.4" />

It's now possible to override some of the metadata on individual events at the point
where you append new events. At this point you can override:

1. `Timestamp` - the time at which the event was appended according to metadata. Many people have requested this over time for both 
   testing scenarios and for importing data from external systems into Marten
2. `Id` - a `Guid` value that isn't used by Marten itself, but might be helpful for being a reference to external commands or in imports
   from non-Marten databases
3. `CorrelationId` & `CausationId`. By default these values are taken from the `IDocumentSession` itself which in turn is trying
   to pull them from any active Open Telemetry span.
4. Header data, but any header value set on the session with the same key overwrites the individual header (for now)

Do note that if you want to potentially overwrite the timestamp of events _and_ you want to use the "QuickAppend" option
for faster appending, you'll need this configuration:

<!-- snippet: sample_setting_quick_with_server_timestamps -->
<a id='snippet-sample_setting_quick_with_server_timestamps'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));

    // This is important!
    opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/MetadataExamples.cs#L79-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_setting_quick_with_server_timestamps' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The setting above is important because the `QuickAppend` normally takes the timestamp from the
database server time at the point of inserting database rows. The `QuickWithServerTimestamps` option changes Marten's
event appending process to take the timestamp data from the application server's `TimeProvider` registered with Marten
by default, or explicitly overridden data on `IEvent` wrappers.

Now, on to event appending. The first way is
to pull out the `IEvent` wrapper and directly setting metadata like this:

<!-- snippet: sample_overriding_event_metadata_by_position -->
<a id='snippet-sample_overriding_event_metadata_by_position'></a>
```cs
public static async Task override_metadata(IDocumentSession session)
{
    var started = new QuestStarted { Name = "Find the Orb" };

    var joined = new MembersJoined
    {
        Day = 2, Location = "Faldor's Farm", Members = new string[] { "Garion", "Polgara", "Belgarath" }
    };

    var slayed1 = new MonsterSlayed { Name = "Troll" };
    var slayed2 = new MonsterSlayed { Name = "Dragon" };

    var joined2 = new MembersJoined { Day = 5, Location = "Sendaria", Members = new string[] { "Silk", "Barak" } };

    var action = session.Events
        .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2);

    // I'm grabbing the IEvent wrapper for the first event in the action
    var wrapper = action.Events[0];
    wrapper.Timestamp = DateTimeOffset.UtcNow.Subtract(1.Hours());
    wrapper.SetHeader("category", "important");
    wrapper.Id = Guid.NewGuid(); // Just showing that you *can* override this value
    wrapper.CausationId = wrapper.CorrelationId = Activity.Current?.Id;

    await session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/MetadataExamples.cs#L15-L44' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_overriding_event_metadata_by_position' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The second option is to directly append the `IEvent` wrappers where you've already
set metadata like this:

<!-- snippet: sample_override_by_appending_the_event_wrapper -->
<a id='snippet-sample_override_by_appending_the_event_wrapper'></a>
```cs
public static async Task override_metadata2(IDocumentSession session)
{
    var started = new QuestStarted { Name = "Find the Orb" };

    var joined = new MembersJoined
    {
        Day = 2, Location = "Faldor's Farm", Members = new string[] { "Garion", "Polgara", "Belgarath" }
    };

    var slayed1 = new MonsterSlayed { Name = "Troll" };
    var slayed2 = new MonsterSlayed { Name = "Dragon" };

    var joined2 = new MembersJoined { Day = 5, Location = "Sendaria", Members = new string[] { "Silk", "Barak" } };

    // The result of this is an IEvent wrapper around the
    // started data with an overridden timestamp
    // and a value for the "color" header
    var wrapper = started.AsEvent()
        .AtTimestamp(DateTimeOffset.UtcNow.Subtract(1.Hours()))
        .WithHeader("color", "blue");

    session.Events
        .StartStream<QuestParty>(wrapper, joined, slayed1, slayed2, joined2);

    await session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/MetadataExamples.cs#L46-L75' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_override_by_appending_the_event_wrapper' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
You can also create event wrappers by calling either:

1. `new Event<T>(T data){ Timestamp = *** }`
2. `var wrapper = Event.For(data);`
:::
