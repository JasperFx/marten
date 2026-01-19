# Aggregate Projections

_Aggregate Projections_ in Marten combine some sort of grouping of events and process them to create a single
aggregated document representing the state of those events. These projections come in two flavors:

**Single Stream Projections** create a rolled up view of all or a segment of the events within a single event stream.
These projections are done either by using the `SingleStreamProjection<TDoc, TId>` base type or by creating a "self aggregating" `Snapshot`
approach with conventional `Create/Apply/ShouldDelete` methods that mutate or evolve the snapshot based on new events.

**Multi Stream Projections** create a rolled up view of a user-defined grouping of events across streams.
These projections are done by sub-classing the `MultiStreamProjection<TDoc, TId>` class and is further described in [Multi-Stream Projections](/events/projections/multi-stream-projections).
An example of a multi-stream projection might be a "query model" within an accounting system of some sort that rolls up
the value of all unpaid invoices by active client. 

You can _also_ use a `MultiStreamProjection` to create views that are a segment of a single stream over time or version. 
Imagine that you have a system that models the activity of a bank account with event sourcing. You could use a `MultiStreamProjection` to create a view that summarizes the activity of a single bank account within a calendar month.

::: tip
The ability to use explicit code to define projections was hugely improved in the Marten 8.0 release.
:::

Within your aggregation projection, you can express the logic about how Marten combines events into a view
through either [conventional methods](/events/projections/conventions) (original, old school Marten) or through [completely explicit code](/events/projections/explicit).

Within an aggregation, you have advanced options to:

* Use event metadata
* Enrich event data with other Marten or external data
* Append all new events or send messages in response to projection updates with [side effects](/events/projections/side-effects)

## Simple Example

The most common usage is to create a "write model" that projects the current state
for a single stream, so on that note, let's jump into a simple example.

::: info
The original author of Marten is huge into epic fantasy book series, hence the silly original problem
domain in the very oldest code samples. Hilariously to him, Marten has fielded and accepted pull requests that
corrected our modeling of the timeline of the Lord of the Rings in sample code.
:::

![Martens on a Quest](/images/martens-on-quest.png "Martens on a Quest")

Let's say that we're building a system to track the progress of a traveling party on a quest within an epic
fantasy series like "The Lord of the Rings" or the "Wheel of Time" and we're using event sourcing to capture
state changes when the "quest party" adds or subtracts members. We might very well need a "write model" for 
the current state of the quest for our command handlers like this one:

<!-- snippet: sample_QuestParty -->
<a id='snippet-sample_questparty'></a>
```cs
public sealed record QuestParty(Guid Id, List<string> Members)
{
    // These methods take in events and update the QuestParty
    public static QuestParty Create(QuestStarted started) => new(started.QuestId, []);
    public static QuestParty Apply(MembersJoined joined, QuestParty party) =>
        party with
        {
            Members = party.Members.Union(joined.Members).ToList()
        };

    public static QuestParty Apply(MembersDeparted departed, QuestParty party) =>
        party with
        {
            Members = party.Members.Where(x => !departed.Members.Contains(x)).ToList()
        };

    public static QuestParty Apply(MembersEscaped escaped, QuestParty party) =>
        party with
        {
            Members = party.Members.Where(x => !escaped.Members.Contains(x)).ToList()
        };
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/DocSamples/EventSourcingQuickstart.cs#L27-L52' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_questparty' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For a little more context, the `QuestParty` above might be consumed in a command handler like this:

<!-- snippet: sample_AddMembers_command_handler -->
<a id='snippet-sample_addmembers_command_handler'></a>
```cs
public record AddMembers(Guid Id, int Day, string Location, string[] Members);

public static class AddMembersHandler
{
    public static async Task HandleAsync(AddMembers command, IDocumentSession session)
    {
        // Fetch the current state of the quest
        var quest = await session.Events.FetchForWriting<QuestParty>(command.Id);
        if (quest.Aggregate == null)
        {
            // Bad quest id, do nothing in this sample case
        }

        var newMembers = command.Members.Where(x => !quest.Aggregate.Members.Contains(x)).ToArray();

        if (!newMembers.Any())
        {
            return;
        }

        quest.AppendOne(new MembersJoined(command.Id, command.Day, command.Location, newMembers));
        await session.SaveChangesAsync();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/DocSamples/EventSourcingQuickstart.cs#L54-L81' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmembers_command_handler' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## How Aggregation Works

::: tip
It's possible to build your own aggregation projections from scratch with the lower level `IProjection` abstraction --
and we've worked with plenty of folks who did over the years -- but just know that the Marten community has invested a lot
of effort over the years into optimizing the internals of the aggregation projections for performance and capability.
:::

::: info
When running with an `Inline` projection lifecycle, the workflow is mostly the same, but Marten can skip the "slicing"
step for single stream projections. By and large, the Marten team recommends almost always running multi-stream projections
asynchronously and probably running single stream projections that utilize enrichment asynchronously as well.
:::

Just to understand a little bit more about the capabilities of Marten's aggregation projections, let's look at the diagram
below that tries to visualize the runtime workflow of aggregation projections inside of the [Async Daemon](/events/projections/async-daemon) background
process:

![How Aggregation Works](/images/aggregation-projection-flow.png "How Aggregation Projections Work")

1. The Daemon is constantly pushing a range of events at a time to an aggregation projection. For example, `Events 1,000 to 2,000 by sequence number`
2. The aggregation "slices" the incoming range of events into a group of `EventSlice` objects that establishes a relationship between the identity
   of an aggregated document and the events that should be applied during this batch of updates for that identity. To be more concrete, a single stream
   projection for `QuestParty` would be creating an `EventSlice` for each quest id it sees in the current range of events. Multi-stream projections
   will have some kind of custom "slicing" or grouping. For example, maybe in our `Quest` tracking system we have a multi-stream projection that
   tries to track how many monsters of each type are defeated. That projection might "slice" by looking for all `MonsterDefeated` events 
   across all streams and group or slice incoming events by the type of monster. The "slicing" logic is automatic for [single stream projections](/events/projections/single-stream-projections), but will require
   explicit configuration or explicitly written logic for [multi stream projections](/events/projections/multi-stream-projections).
3. Once the projection has a known list of all the aggregate documents that will be updated by the current range of events, the projection will
   fetch each persisted document, first from any active aggregate cache in memory, then by making a single batched request to the 
   Marten document storage for any missing documents and adding these to any active cache (see [Optimizing Performance](/events/optimizing) for more information about the potential caching). 
4. The projection will execute any [event enrichment](/events/projections/enrichment) against the now known group of `EventSlice`. This process gives you a hook to 
   efficiently "enrich" the raw event data with extra data lookups from Marten document storage or even other sources.
5. Most of the work as a developer is in the application or "Evolve" step of the diagram above. After the "slicing", the aggregation has turned the range of raw event data into
   `EventSlice` objects that contain the current snapshot of a projected document by its identity (if one exists), the identity itself, and the events from within that original range
   that should be applied on top of the current snapshot to "evolve" it to reflect those events. This can be coded either with the conventional [Apply/Create/ShouldDelete methods](/events/projections/conventions) or using [explicit code](/events/projections/explicit) --
   which is almost inevitably means a `switch` statement. Using the `QuestParty` example again, the aggregation projection would get an `EventSlice` that contains the identity of
   an active quest, the snapshot of the current `QuestParty` document that is persisted by Marten, and the new `MembersJoined` et al events that should be applied to the existing
   `QuestParty` object to derive the new version of `QuestParty`. 
6. _Just_ before Marten persists all the changes from the application / evolve step, you have the [`RaiseSideEffects()` hook](/events/projections/side-effects) to potentially raise "side effects" like
   appending additional events based on the now updated state of the projected aggregates or publishing the new state of an aggregate through messaging ([Wolverine](https://wolverinefx.net/guide/durability/marten/) has first class support for Marten projection side effects through its Marten integration into the full "Critter Stack")
7. For the current event range and event slices, Marten will send all aggregate document updates or deletions, new event appending operations, and even outboxed, outgoing messages sent via side effects
   (if you're using the Wolverine integration) in batches to the underlying PostgreSQL database. I'm calling this out because we've constantly found in
   Marten development that command batching to PostgreSQL is a huge factor in system performance and the async daemon has been designed to try to minimize the number of network round trips between your application and PostgreSQL at every turn.
8. Assuming the transaction succeeds for the current event range and the operation batch in the previous step, Marten will call "after commit" observers. This notification for example will
   release any messages raised as a side effect and actually send those messages via whatever is doing the actual publishing (probably Wolverine).

::: tip
Marten happily supports immutable data types for the aggregate documents produced by projections, but also happily supports
mutable types as well. The usage of the application code is a little different though.
:::

::: info
Starting with Marten 8.0, we've tried somewhat to conform to the terminology used by the [Functional Event Sourcing Decider](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider) paper by Jeremie Chassaing. To that end, the API now refers to a "snapshot" that really just means _a_ version of the projection and "evolve" as the step of applying new events to an existing "snapshot" to calculate a new "snapshot."
:::

## Aggregate Caching

See the content on aggregate caching in [Optimizing Performance](/events/optimizing).

## Strong Typed Identifiers <Badge type="tip" text="7.29" />

::: info
The rise of Strong Typed Identifiers has not been the most pleasant experience for the Marten and
Wolverine teams as these types are "neither fish, nor fowl" in the way the internals have to constantly
wrap or unwrap these things. As the technical leader of Marten is of the Gen X cohort, Jeremy believes
[this movie scene](https://www.youtube.com/watch?v=350kq0anjq0) exactly encapsulates his feelings about the work we've had to do to support Strong Typed
Identifiers throughout the "Critter Stack."
:::

Marten supports using strong-typed identifiers as the document identity for aggregated documents. Here's an example:

<!-- snippet: sample_using_strong_typed_identifier_for_aggregate_projections -->
<a id='snippet-sample_using_strong_typed_identifier_for_aggregate_projections'></a>
```cs
[StronglyTypedId(Template.Guid)]
public readonly partial struct PaymentId;

public class Payment
{
    [JsonInclude] public PaymentId? Id { get; private set; }

    [JsonInclude] public DateTimeOffset CreatedAt { get; private set; }

    [JsonInclude] public PaymentState State { get; private set; }

    public static Payment Create(IEvent<PaymentCreated> @event)
    {
        return new Payment
        {
            Id = new PaymentId(@event.StreamId), CreatedAt = @event.Data.CreatedAt, State = PaymentState.Created
        };
    }

    public void Apply(PaymentCanceled @event)
    {
        State = PaymentState.Canceled;
    }

    public void Apply(PaymentVerified @event)
    {
        State = PaymentState.Verified;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/using_guid_based_strong_typed_id_for_aggregate_identity.cs#L141-L173' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_strong_typed_identifier_for_aggregate_projections' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Just note that for single stream aggregations, your strong typed identifier types will need to wrap either a `Guid` or
`string` depending on your application's `StreamIdentity`. 

At this point, the `FetchForWriting` and `FetchForLatest` APIs do not directly support strongly typed identifiers and you
will have to just pass in the wrapped, primitive value like this:

<!-- snippet: sample_use_fetch_for_writing_with_strong_typed_identifier -->
<a id='snippet-sample_use_fetch_for_writing_with_strong_typed_identifier'></a>
```cs
private async Task use_fetch_for_writing_with_strong_typed_identifier(PaymentId id, IDocumentSession session)
{
    var stream = await session.Events.FetchForWriting<Payment>(id.Value);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/using_guid_based_strong_typed_id_for_aggregate_identity.cs#L94-L101' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_use_fetch_for_writing_with_strong_typed_identifier' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Aggregate by Stream

See [Single Stream Projections and Snapshots](./single-stream-projections).

## Stream Aggregations

See [Single Stream Projections and Snapshots](./single-stream-projections).

## Using Event Metadata

You can incorporate the event metadata that Marten collects within the aggregation projection.

Read more about that in [Using Metadata](./using-metadata).

## Raising Events, Messages, or other Operations in Aggregation Projections <Badge type="tip" text="7.27" />

See [Side Effects](./side-effects) for more information.
