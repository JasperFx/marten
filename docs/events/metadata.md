# Event Metadata

See [Marten Metadata](/documents/metadata) for more information and examples
about capturing metadata as part of `IDocumentSession` unit of work operations.

The metadata tracking for events can be extended in Marten by opting into extra fields
for causation, correlation, user names, and key/value headers with this syntax as part of configuring
Marten:

<!-- snippet: sample_ConfigureEventMetadata -->
<a id='snippet-sample_ConfigureEventMetadata'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("connection string");

    // This adds additional metadata tracking to the
    // event store tables
    opts.Events.MetadataConfig.HeadersEnabled = true;
    opts.Events.MetadataConfig.CausationIdEnabled = true;
    opts.Events.MetadataConfig.CorrelationIdEnabled = true;
    opts.Events.MetadataConfig.UserNameEnabled = true;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L118-L132' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ConfigureEventMetadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

By default, Marten runs "lean" by omitting the extra metadata storage on events shown above. Causation, correlation, user name (last modified by), and header fields must be individually enabled. 
Event the database table columns for this data will not be created unless you opt in 

When appending events, Marten will automatically tag events with the data from these properties
on the `IDocumentSession` when capturing the new events:

<!-- snippet: sample_query_session_metadata_tracking -->
<a id='snippet-sample_query_session_metadata_tracking'></a>
```cs
public string? CausationId { get; set; }
public string? CorrelationId { get; set; }

public string TenantId { get; protected set; }
public string CurrentUserName { get; set; }

public string? LastModifiedBy
{
    get => CurrentUserName;
    set => CurrentUserName = value;
}

/// <summary>
///     This is meant to be lazy created, and can be null
/// </summary>
public Dictionary<string, object>? Headers { get; protected set; }
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Internal/Sessions/QuerySession.Metadata.cs#L15-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_session_metadata_tracking' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
Open Telemetry `Activity` (spans) are only emitted if there is an active listener for your application.
:::

In the data elements above, the correlation id and causation id is taken automatically from any active Open Telemetry span,
so these values should just flow from ASP.Net Core requests or typical message bus handlers (like Wolverine!) when Open Telemetry
spans are enabled and being emitted.

Values for `IDocumentSession.LastModifiedBy` and `IDocumentSession.Headers` will need to be set manually, but once they
are, those values will flow through to new events captured by a session when `SaveChangesAsync()` is called.

::: tip
The basic [IEvent](https://github.com/JasperFx/jasperfx/blob/main/src/JasperFx.Events/Event.cs#L34-L176) abstraction and quite a bit of other generic
event sourcing code moved in Marten 8.0 to the shared JasperFx.Events library.
:::

The actual metadata is accessible from the `IEvent` interface event wrappers as shown below (which are implemented by `Event<T>`):

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
    ///     JasperFx.Event's type alias string for the Event type
    /// </summary>
    string EventTypeName { get; set; }

    /// <summary>
    ///     JasperFx.Events's string representation of the event type
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
    ///     JasperFx.Events's name for the aggregate type that will be persisted
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
    
    /// <summary>
    /// Build a Func that can resolve an identity from the IEvent and even
    /// handles the dastardly strong typed identifiers
    /// </summary>
    /// <typeparam name="TId"></typeparam>
    /// <returns></returns>
    /// <exception cref="NotSupportedException"></exception>
    public static Func<IEvent, TId> CreateAggregateIdentitySource<TId>()
        where TId : notnull
    {
        if (typeof(TId) == typeof(Guid)) return e => e.StreamId.As<TId>();
        if (typeof(TId) == typeof(string)) return e => e.StreamKey!.As<TId>();
        
        var valueTypeInfo = ValueTypeInfo.ForType(typeof(TId));
        
        var e = Expression.Parameter(typeof(IEvent), "e");
        var eMember = valueTypeInfo.SimpleType == typeof(Guid)
            ? ReflectionHelper.GetProperty<IEvent>(x => x.StreamId)
            : ReflectionHelper.GetProperty<IEvent>(x => x.StreamKey!);

        var raw = Expression.Call(e, eMember.GetMethod!);
        Expression? wrapped = null;
        if (valueTypeInfo.Builder != null)
        {
            wrapped = Expression.Call(null, valueTypeInfo.Builder, raw);
        }
        else if (valueTypeInfo.Ctor != null)
        {
            wrapped = Expression.New(valueTypeInfo.Ctor, raw);
        }
        else
        {
            throw new NotSupportedException("Cannot build a type converter for strong typed id type " +
                                            valueTypeInfo.OuterType.FullNameInCode());
        }

        var lambda = Expression.Lambda<Func<IEvent, TId>>(wrapped, e);

        return lambda.CompileFast();
    }
    
    /// <summary>
    ///     Optional metadata describing the user name or
    ///     process name for the unit of work that captured this event
    /// </summary>
    string? UserName { get; set; }
    
    /// <summary>
    /// No, this is *not* idiomatic event sourcing, but this may be used as metadata to direct
    /// projection replays or subscription rewinding as an event that should not be used
    /// </summary>
    bool IsSkipped { get; set; }
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
