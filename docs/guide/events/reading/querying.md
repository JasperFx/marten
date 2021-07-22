# Querying Event and Stream Data

## Fetch Events for a Stream

You can retrieve the events for a single stream at any time with the `IEventStore.FetchStream()` methods shown below:

<!-- snippet: sample_using-fetch-stream -->
<a id='snippet-sample_using-fetch-stream'></a>
```cs
public void load_event_stream(IDocumentSession session, Guid streamId)
{
    // Fetch *all* of the events for this stream
    var events1 = session.Events.FetchStream(streamId);

    // Fetch the events for this stream up to and including version 5
    var events2 = session.Events.FetchStream(streamId, 5);

    // Fetch the events for this stream at this time yesterday
    var events3 = session.Events
                .FetchStream(streamId, timestamp: DateTime.UtcNow.AddDays(-1));
}

public async Task load_event_stream_async(IDocumentSession session, Guid streamId)
{
    // Fetch *all* of the events for this stream
    var events1 = await session.Events.FetchStreamAsync(streamId);

    // Fetch the events for this stream up to and including version 5
    var events2 = await session.Events.FetchStreamAsync(streamId, 5);

    // Fetch the events for this stream at this time yesterday
    var events3 = await session.Events
                .FetchStreamAsync(streamId, timestamp: DateTime.UtcNow.AddDays(-1));
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/event_store_quickstart.cs#L103-L130' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-fetch-stream' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The data returned is a list of `IEvent` objects, where each is a strongly-typed `Event<T>` object shown below:

<!-- snippet: sample_IEvent -->
<a id='snippet-sample_ievent'></a>
```cs
public interface IEvent
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

    /// <summary>
    /// Has this event been archived and no longer applicable
    /// to projected views
    /// </summary>
    bool IsArchived { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Events/Event.cs#L8-L105' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ievent' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Stream State

If you just need to check on the state of an event stream - what version (effectively the number of events in the stream) it is and what, if any, aggregate type it represents - you can use the `IEventStore.FetchStreamState()/FetchStreamStateAsync()` methods or `IBatchQuery.Events.FetchStreamState()`, as shown below:

<!-- snippet: sample_fetching_stream_state -->
<a id='snippet-sample_fetching_stream_state'></a>
```cs
public class fetching_stream_state: IntegrationContext
{
    private Guid theStreamId;

    public fetching_stream_state(DefaultStoreFixture fixture) : base(fixture)
    {
        var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        theStreamId = theSession.Events.StartStream<Quest>(joined, departed).Id;
        theSession.SaveChanges();
    }

    [Fact]
    public void can_fetch_the_stream_version_and_aggregate_type()
    {
        var state = theSession.Events.FetchStreamState(theStreamId);

        state.Id.ShouldBe(theStreamId);
        state.Version.ShouldBe(2);
        state.AggregateType.ShouldBe(typeof(Quest));
        state.LastTimestamp.ShouldNotBe(DateTime.MinValue);
        state.Created.ShouldNotBe(DateTime.MinValue);
    }

    [Fact]
    public async Task can_fetch_the_stream_version_and_aggregate_type_async()
    {
        var state = await theSession.Events.FetchStreamStateAsync(theStreamId);

        state.Id.ShouldBe(theStreamId);
        state.Version.ShouldBe(2);
        state.AggregateType.ShouldBe(typeof(Quest));
        state.LastTimestamp.ShouldNotBe(DateTime.MinValue);
        state.Created.ShouldNotBe(DateTime.MinValue);
    }

    [Fact]
    public async Task can_fetch_the_stream_version_through_batch_query()
    {
        var batch = theSession.CreateBatchQuery();

        var stateTask = batch.Events.FetchStreamState(theStreamId);

        await batch.Execute();

        var state = await stateTask;

        state.Id.ShouldBe(theStreamId);
        state.Version.ShouldBe(2);
        state.AggregateType.ShouldBe(typeof(Quest));
        state.LastTimestamp.ShouldNotBe(DateTime.MinValue);
    }

    [Fact]
    public async Task can_fetch_the_stream_events_through_batch_query()
    {
        var batch = theSession.CreateBatchQuery();

        var eventsTask = batch.Events.FetchStream(theStreamId);

        await batch.Execute();

        var events = await eventsTask;

        events.Count.ShouldBe(2);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/fetching_stream_state.cs#L84-L154' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_fetching_stream_state' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Furthermore, `StreamState` contains metadata for when the stream was created, `StreamState.Created`, and when the stream was last updated, `StreamState.LastTimestamp`.

## Fetch a Single Event

You can fetch the information for a single event by id, including its version number within the stream, by using `IEventStore.Load()` as shown below:

<!-- snippet: sample_load-a-single-event -->
<a id='snippet-sample_load-a-single-event'></a>
```cs
public void load_a_single_event_synchronously(IDocumentSession session, Guid eventId)
{
    // If you know what the event type is already
    var event1 = session.Events.Load<MembersJoined>(eventId);

    // If you do not know what the event type is
    var event2 = session.Events.Load(eventId);
}

public async Task load_a_single_event_asynchronously(IDocumentSession session, Guid eventId)
{
    // If you know what the event type is already
    var event1 = await session.Events.LoadAsync<MembersJoined>(eventId);

    // If you do not know what the event type is
    var event2 = await session.Events.LoadAsync(eventId);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/event_store_quickstart.cs#L132-L151' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_load-a-single-event' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Querying Directly Against Event Data

We urge caution about this functionality because it requires a search against the entire `mt_events` table. To issue Linq queries against any specific event type, use the method shown below:

<!-- snippet: sample_query-against-event-data -->
<a id='snippet-sample_query-against-event-data'></a>
```cs
[Fact]
public void can_query_against_event_type()
{
    theSession.Events.StartStream<Quest>(joined1, departed1);
    theSession.Events.StartStream<Quest>(joined2, departed2);

    theSession.SaveChanges();

    theSession.Events.QueryRawEventDataOnly<MembersJoined>().Count().ShouldBe(2);
    theSession.Events.QueryRawEventDataOnly<MembersJoined>().ToArray().SelectMany(x => x.Members).Distinct()
        .OrderBy(x => x)
        .ShouldHaveTheSameElementsAs("Egwene", "Matt", "Nynaeve", "Perrin", "Rand", "Thom");

    theSession.Events.QueryRawEventDataOnly<MembersDeparted>()
        .Single(x => x.Members.Contains("Matt")).Id.ShouldBe(departed2.Id);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/query_against_event_documents_Tests.cs#L23-L41' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query-against-event-data' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can use any Linq operator that Marten supports to query against event data. We think that this functionality is probably more useful for diagnostics or troubleshooting rather than something you would routinely use to support your application. We recommend that you favor event projection views over querying within the raw event table.

With Marten 1.0, you can issue queries with Marten's full Linq support against the raw event data with this method:

<!-- snippet: sample_example_of_querying_for_event_data -->
<a id='snippet-sample_example_of_querying_for_event_data'></a>
```cs
public void example_of_querying_for_event_data(IDocumentSession session, Guid stream)
{
    var events = session.Events.QueryAllRawEvents()
        .Where(x => x.StreamId == stream)
        .OrderBy(x => x.Sequence)
        .ToList();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/query_against_event_documents_Tests.cs#L158-L167' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_example_of_querying_for_event_data' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This mechanism will allow you to query by any property of the `IEvent` interface shown above.
