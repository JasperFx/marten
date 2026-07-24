# Side Effects <Badge type="tip" text="7.27" />

::: tip
This functionality was originally meant for asynchronous projections running 
in the background and that is still where the Marten team thinks this functionality
fits best, but there is now an option to use side effects with `Inline` projections
:::

_Sometimes_, it can be valuable to emit new events during the processing of a projection
when you first know the new state of the projected aggregate documents. Or maybe what you might want to do is to send
a message for the new state of an updated projection. Here's a couple possible scenarios that might lead you here:

- There's some kind of business logic that can be processed against an aggregate to "decide" what the system can do next
- You need to send updates about the aggregated projection state to clients via web sockets
- You need to replicate the Marten projection data in a completely different database
- There are business processes that can be kicked off for updates to the aggregated state

To do any of this, you can override the `RaiseSideEffects()` method in any aggregated projection that uses one of the
following base classes:

1. `SingleStreamProjection`
2. `MultiStreamProjection`

Here's an example of that method overridden in a projection:

<!-- snippet: sample_aggregation_using_event_metadata -->
<a id='snippet-sample_aggregation_using_event_metadata'></a>
```cs
public partial class TripProjection: SingleStreamProjection<Trip, Guid>
{
    // Access event metadata through IEvent<T>
    public Trip Create(IEvent<TripStarted> @event)
    {
        var trip = new Trip
        {
            Id = @event.StreamId, // Marten does this for you anyway
            Started = @event.Timestamp,
            CorrelationId = @event.Timestamp, // Open telemetry type tracing
            Description = @event.Data.Description // Still access to the event body
        };

        // Use a custom header
        if (@event.Headers.TryGetValue("customer", out var customerId))
        {
            trip.CustomerId = (string)customerId;
        }

        return trip;
    }

    public void Apply(TripEnded ended, Trip trip, IEvent @event)
    {
        trip.Ended = @event.Timestamp;
    }

    // Other Apply/ShouldDelete methods

    public override ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<Trip> slice)
    {
        // Emit other events or messages during asynchronous projection
        // processing

        // Access to the current state as of the projection
        // event page being processed *right* now
        var currentTrip = slice.Snapshot;

        if (currentTrip.TotalMiles > 1000)
        {
            // Append a new event to this stream
            slice.AppendEvent(new PassedThousandMiles());

            // Append a new event to a different event stream by
            // first specifying a different stream id
            slice.AppendEvent(currentTrip.InsuranceCompanyId, new IncrementThousandMileTrips());

            // "Publish" outgoing messages when the event page is successfully committed
            slice.PublishMessage(new SendCongratulationsOnLongTrip(currentTrip.Id));

            // And yep, you can make additional changes to Marten
            operations.Store(new CompletelyDifferentDocument
            {
                Name = "New Trip Segment",
                OriginalTripId = currentTrip.Id
            });
        }

        // This usage has to be async in case you're
        // doing any additional data access with the
        // Marten operations
        return new ValueTask();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/TripProjectionWithEventMetadata.cs#L31-L98' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregation_using_event_metadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Recovering the Slice Identity for Deleted Aggregates <Badge type="tip" text="9.19" />

The `RaiseSideEffects()` override shown above receives the current `slice.Snapshot`, which is the most
convenient way to read the aggregate identity. That works well right up until the aggregate is _deleted_ in
the same batch that is raising side effects — at that point `slice.Snapshot` is `null` and there is no
document to read the identity from. This bites hardest in a `MultiStreamProjection`, where the slice groups
events drawn from many different streams: `slice.Events().First().StreamId` is one of the _member_ stream
ids, not the grouped aggregate identity, so there was previously no reliable way to recover the key of a
just-deleted slice in order to emit a follow-on event or publish a message.

Starting with Marten 9.19 (JasperFx.Events 2.35.0) there is a second `RaiseSideEffects()` overload that adds
a strongly-typed `id` parameter carrying the slice identity — and it is populated even when
`slice.Snapshot == null`:

<!-- snippet: sample_raise_side_effects_with_slice_id -->
<a id='snippet-sample_raise_side_effects_with_slice_id'></a>
```cs
// A running count of the active members of a team, aggregated across many
// separate per-member event streams by TeamId.
public class TeamRoster
{
    public Guid Id { get; set; }
    public int MemberCount { get; set; }
}

public record MemberJoinedTeam(Guid TeamId);
public record MemberLeftTeam(Guid TeamId);
public record TeamDisbanded(Guid TeamId);

// The message we want to publish when a team is closed out
public record NotifyTeamClosed(Guid TeamId);

public partial class TeamRosterProjection: MultiStreamProjection<TeamRoster, Guid>
{
    public TeamRosterProjection()
    {
        // All three event types are grouped by TeamId, which becomes the
        // identity of each slice/aggregate.
        Identity<MemberJoinedTeam>(x => x.TeamId);
        Identity<MemberLeftTeam>(x => x.TeamId);
        Identity<TeamDisbanded>(x => x.TeamId);
    }

    public void Apply(TeamRoster roster, MemberJoinedTeam _) => roster.MemberCount++;
    public void Apply(TeamRoster roster, MemberLeftTeam _) => roster.MemberCount--;

    // When a team is disbanded the aggregate is deleted, so the snapshot handed to
    // RaiseSideEffects below is null.
    public bool ShouldDelete(TeamDisbanded _) => true;

    // NEW in JasperFx.Events 2.35.0: the second parameter hands you the identity
    // of the current slice even when its snapshot has been deleted in this batch.
    public override ValueTask RaiseSideEffects(IDocumentOperations operations, Guid id,
        IEventSlice<TeamRoster> slice)
    {
        if (slice.Snapshot == null)
        {
            // The aggregate was deleted (TeamDisbanded -> ShouldDelete(...) == true),
            // so there is no snapshot to read the identity from. Before this overload,
            // there was no reliable way to recover the team identity here -- the slice
            // groups events from many different streams, so slice.Events().First().StreamId
            // is a *member* stream id, not the TeamId. The id parameter is the slice
            // identity (the TeamId) regardless of whether the snapshot still exists.
            slice.PublishMessage(new NotifyTeamClosed(id));
            return new ValueTask();
        }

        // Normal, non-deleted processing still has full access to the current snapshot
        // (and, of course, to id)...

        return new ValueTask();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/raise_side_effects_with_slice_id.cs#L75-L134' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_raise_side_effects_with_slice_id' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The two overloads are fully backwards compatible: the original two-argument
`RaiseSideEffects(IDocumentOperations, IEventSlice<TDoc>)` still works exactly as before, and the default
implementation of the new three-argument overload simply delegates to it. Override the new
`RaiseSideEffects(IDocumentOperations operations, TId id, IEventSlice<TDoc> slice)` overload whenever you need
the aggregate identity independently of the snapshot — most commonly to react to a deletion — and keep using
the original overload for everything else.

A couple important facts about this new functionality:

- The `RaiseSideEffects()` method is only called during _continuous_ asynchronous projection execution, and will not
  be called during projection rebuilds or `Inline` projection usage **unless you explicitly enable this behavior as shown below**
- Side effects are also suppressed during the **blue/green warm-up phase** of a gated new projection version. When a
  projection opts into `GateSideEffectsBehindPriorVersion`, a freshly deployed version first replays history the prior
  version already processed in Rebuild mode (no side effects) and only fires side effects for events past the prior
  version's mark. See [Suppressing Side Effects During the Blue/Green Warm-up](/events/projections/rebuilding#suppressing-side-effects-during-the-blue-green-warm-up).
- Events emitted during the side effect method are _not_ immediately applied to the current projected document value by Marten
- You _can_ alter the aggregate value or replace it yourself in this side effect method to reflect new events, but the onus
  is on you the user to apply idempotent updates to the aggregate based on these new events in the actual handlers for
  the new events when those events are handled by the daemon in a later batch
- There is a [Wolverine](https://wolverinefx.net) integration (of course) to publish the messages through Wolverine if using the `AddMarten()IntegrateWithWolverine()` option

This relatively new behavior that was built for a specific [JasperFx Software](https://jasperfx.net) client project,
but has been on the backlog for quite some time. If there are any difficulties with this approach, please feel free
to join the [Marten Discord room](https://discord.gg/BGkCDx5d).

## Side Effects in Inline Projections <Badge type="tip" text="7.40" />

By default, Marten will only process projection "side effects" during continuous asynchronous processing. However, if you
wish to use projection side effects while running projections with an `Inline` lifecycle, you can do that with this setting:

<!-- snippet: sample_using_enablesideeffectsoninlineprojections -->
<a id='snippet-sample_using_enablesideeffectsoninlineprojections'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));

    // This is your magic setting to tell Marten to process any projection
    // side effects even when running Inline
    opts.Events.EnableSideEffectsOnInlineProjections = true;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/UsingInlineSideEffects.cs#L12-L24' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_enablesideeffectsoninlineprojections' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This functionality was originally written as a way of sending external messages to a separate system carrying the new state of a single stream projection
any time new events were captured on an event stream. 
