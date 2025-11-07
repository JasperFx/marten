# Event Metadata

See [Marten Metadata](/documents/metadata) for more information and examples
about capturing metadata as part of `IDocumentSession` unit of work operations.

The metadata tracking for events can be extended in Marten by opting into extra fields
for causation, correlation, user names, and key/value headers with this syntax as part of configuring
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
    opts.Events.MetadataConfig.UserNameEnabled = true;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L118-L132' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configureeventmetadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

By default, Marten runs "lean" by omitting the extra metadata storage on events shown above. Causation, correlation, user name (last modified by), and header fields must be individually enabled. 
The database table columns for this data will not be created unless you opt-in.

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

The `CorrelationId` and `CausationId` is taken automatically from any active OpenTelemetry span,
so these values should just flow from ASP.NET Core requests or typical message bus handlers (like Wolverine!) when OpenTelemetry
spans are enabled and being emitted.

Values for `IDocumentSession.LastModifiedBy` and `IDocumentSession.Headers` will need to be set manually, but once they
are, those values will flow through to new events captured by a session when `SaveChangesAsync()` is called.

The actual metadata is accessible from the [IEvent](https://github.com/JasperFx/jasperfx/blob/main/src/JasperFx.Events/Event.cs#L34-L176) interface wrapper as shown (which is implemented by `Event<T>`).

<!-- snippet: sample_query_event_metadata -->
<a id='snippet-sample_query_event_metadata'></a>
```cs
// Apply metadata to the IDocumentSession
theSession.CorrelationId = "The Correlation";
theSession.CausationId = "The Cause";
theSession.LastModifiedBy = "Last Person";
theSession.SetHeader("HeaderKey", "HeaderValue");

var streamId = theSession.Events
    .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
await theSession.SaveChangesAsync();

var events = await theSession.Events.FetchStreamAsync(streamId);
events.Count.ShouldBe(5);
// Inspect metadata
events.ShouldAllBe(e =>
    e.Headers != null && e.Headers.ContainsKey("HeaderKey") && "HeaderValue".Equals(e.Headers["HeaderKey"]));
events.ShouldAllBe(e => e.CorrelationId == "The Correlation");
events.ShouldAllBe(e => e.CausationId == "The Cause");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/fetch_a_single_event_with_metadata.cs#L38-L56' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_event_metadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
 To utilize metadata within Projections, see [Using Event Metadata in Aggregates](/events/projections/aggregate-projections#using-event-metadata-in-aggregates).
:::

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
