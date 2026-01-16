# Single Stream Projections and Snapshots

::: tip
Definitely check out the content on [CQRS Command Handler Workflow for Capturing Events](/scenarios/command_handler_workflow)
and [Reading Aggregates](/events/projections/read-aggregates) to get the best possible performance and
development usability for aggregate projections with Marten. Also see the combination with [Wolverine](https://wolverinefx.net)
in its [Aggregate Handler Workflow](https://wolverinefx.net/guide/durability/marten/event-sourcing.html) for literally the
lowest code ceremony possible to use Marten within a CQRS architecture.
:::

::: tip
Projection *document* types need to be scoped as public because of Marten's internal code generation techniques. Some methods
discovered by the method conventions can be internal or private, but the holding type must be public. If you really
care deeply about marking types as `internal`, just use the explicit code options.
:::

Single stream projections (i.e., a projected view of the events within a single event stream)
are aggregations that roll up the events for a single stream into a projected view. Starting with the simplest possible
approach and a simplistic workflow, let's revisit the `QuestParty` event modeling with a "self-aggregating" `QuestParty`:

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

Note the usage of the `Apply()` and `Create()` methods directly on the `QuestParty` type. Marten can use those methods to "evolve"
the projected `QuestParty` objects with new events. With a "self-aggregating" aggregate type, you would register that
with Marten like this:

<!-- snippet: sample_registering_snapshots -->
<a id='snippet-sample_registering_snapshots'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));

    // Just for the sake of completeness, "self-aggregating" types
    // can be registered as projections in Marten with this syntax
    // where "Snapshot" now means "a version of the projection from the events"
    opts.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Inline);
    opts.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Async);

    // This is the equivalent of ProjectionLifecycle.Live
    // This is no longer necessary with Marten 8, but may be necessary
    // for *future* optimizations
    opts.Projections.LiveStreamAggregation<QuestParty>();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/DocSamples/RegisteringProjections.cs#L50-L69' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_snapshots' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: info
See [Using Conventional Methods](/events/projections/conventions) for more information about the conventions.
:::

::: tip
If you call `AggregateStreamAsync<T>()`, `FetchLatest<T>()`, or `FetchForWriting<T>()` with a type "T" that is not
registered, Marten will try to treat the "T" as a self-aggregating snapshot with a `Live` lifecycle. That just means
that Marten is always fetching the event data into memory and applying the events in memory to create the data for
your snapshot "T" on the fly. That's perfectly appropriate for short streams, but maybe a performance issue in longer
event streams.
:::

If you don't like putting the conventional methods directly on the projected types, or need to use some of the 
more advanced settings for projections, you can move those `Apply` or `Create` methods to a separate type that
inherits from the `SingleStreamProjection<TDoc, TId>` base type like this:

<!-- snippet: sample_TripProjection_aggregate -->
<a id='snippet-sample_tripprojection_aggregate'></a>
```cs
public class TripProjection: SingleStreamProjection<Trip, Guid>
{
    public TripProjection()
    {
        DeleteEvent<TripAborted>();

        DeleteEvent<Breakdown>(x => x.IsCritical);

        DeleteEvent<VacationOver>((trip, _) => trip.Traveled > 1000);
    }

    // These methods can be either public, internal, or private but there's
    // a small performance gain to making them public
    public void Apply(Arrival e, Trip trip) => trip.State = e.State;

    public void Apply(Travel e, Trip trip)
    {
        Debug.WriteLine($"Trip {trip.Id} Traveled " + e.TotalDistance());
        trip.Traveled += e.TotalDistance();
        Debug.WriteLine("New total distance is " + e.TotalDistance());
    }

    public void Apply(TripEnded e, Trip trip)
    {
        trip.Active = false;
        trip.EndedOn = e.Day;
    }

    public Trip Create(IEvent<TripStarted> started)
    {
        return new Trip { Id = started.StreamId, StartedOn = started.Data.Day, Active = true };
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TestingSupport/TripProjectionWithCustomName.cs#L48-L84' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tripprojection_aggregate' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And register that projection like this:

::: tip
Remember to start the Async Daemon when using async projections, see [Asynchronous Projections Daemon](/events/projections/async-daemon.html)
:::

<!-- snippet: sample_registering_an_aggregate_projection -->
<a id='snippet-sample_registering_an_aggregate_projection'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Register as inline
    opts.Projections.Add<TripProjection>(ProjectionLifecycle.Inline);

    // Or instead, register to run asynchronously
    opts.Projections.Add<TripProjection>(ProjectionLifecycle.Async);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TestingSupport/TripProjectionWithCustomName.cs#L20-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_an_aggregate_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do notice the usage of the `DeleteEvent<T>()` method in the constructor function of `TripProjection`. You can also tell
Marten that you're deleting the projected document by just returning `null` from an `Evolve()` method, but the `DeleteEvent<T>()`
marker is a little bit of an optimization that short circuits the projection processing.

Or finally, you can use [explicit code](/events/projections/explicit) to define your single stream projection. You'll
still inherit from `SingleStreamProjection<TDoc, TId>`, but this time override *one and only one* of these methods:

1. `Evolve` -- simple workflows where all you ever do is create, update, or delete projected views with just the event data
2. `EvolveAsync` -- `Evolve`, but with the ability to look up extra data with `IQuerySession`
3. `DetermineAction` -- more complex workflows where you might have reentrant states or utilize [soft deletes](/documents/deletes) for the persisted projection data
4. `DetermineActionAsync` -- `DetermineAction`, but with access to `IQuerySession` for extra data look ups during projection

Here's a simple example of explicit code in projections:

<!-- snippet: sample_AppointmentProjection -->
<a id='snippet-sample_appointmentprojection'></a>
```cs
public class AppointmentProjection: SingleStreamProjection<Appointment, Guid>
{
    public AppointmentProjection()
    {
        // Make sure this is turned on!
        Options.CacheLimitPerTenant = 1000;
    }

    public override Appointment Evolve(Appointment snapshot, Guid id, IEvent e)
    {
        switch (e.Data)
        {
            case AppointmentRequested requested:
                snapshot = new Appointment()
                {
                    Status = AppointmentStatus.Requested,
                    Requirement = new Licensing(requested.SpecialtyCode, requested.StateCode),
                    PatientId = requested.PatientId,
                    Created = e.Timestamp,
                    SpecialtyCode = requested.SpecialtyCode
                };
                break;

            case AppointmentRouted routed:
                snapshot.BoardId = routed.BoardId;
                break;

            case ProviderAssigned assigned:
                snapshot.ProviderId = assigned.ProviderId;
                break;

            case AppointmentEstimated estimated:
                snapshot.Status = AppointmentStatus.Scheduled;
                snapshot.EstimatedTime = estimated.Time;
                break;

            case AppointmentStarted:
                snapshot.Status = AppointmentStatus.Started;
                snapshot.Started = e.Timestamp;
                break;

            case AppointmentCompleted:
                snapshot.Status = AppointmentStatus.Completed;
                snapshot.Completed = e.Timestamp;
                break;
        }

        return snapshot;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TeleHealth/Appointments.cs#L40-L93' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_appointmentprojection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And a more complicated sample from our tests that just shows how you can create a reentrant workflow that includes
the possibility of soft deleting and later "un-deleting" the projected document in storage:

<!-- snippet: sample_custom_aggregate_with_start_and_stop -->
<a id='snippet-sample_custom_aggregate_with_start_and_stop'></a>
```cs
public class StartAndStopProjection: SingleStreamProjection<StartAndStopAggregate, Guid>
{
    public StartAndStopProjection()
    {
        // This is an optional, but potentially important optimization
        // for the async daemon so that it sets up an allow list
        // of the event types that will be run through this projection
        IncludeType<Start>();
        IncludeType<End>();
        IncludeType<Restart>();
        IncludeType<Increment>();
    }

    public override (StartAndStopAggregate?, ActionType) DetermineAction(StartAndStopAggregate? snapshot, Guid identity,
        IReadOnlyList<IEvent> events)
    {
        var actionType = ActionType.Store;

        if (snapshot == null && events.HasNoEventsOfType<Start>())
        {
            return (snapshot, ActionType.Nothing);
        }

        var eventData = events.ToQueueOfEventData();
        while (eventData.Any())
        {
            var data = eventData.Dequeue();
            switch (data)
            {
                case Start:
                    snapshot = new StartAndStopAggregate
                    {
                        // Have to assign the identity ourselves
                        Id = identity
                    };
                    break;

                case Increment when snapshot is { Deleted: false }:

                    if (actionType == ActionType.StoreThenSoftDelete)
                        continue;

                    // Use explicit code to only apply this event
                    // if the snapshot already exists
                    snapshot.Increment();
                    break;

                case End when snapshot is { Deleted: false }:
                    // This will be a "soft delete" because the snapshot type
                    // implements the IDeleted interface
                    snapshot.Deleted = true;
                    actionType = ActionType.StoreThenSoftDelete;
                    break;

                case Restart when snapshot == null || snapshot.Deleted:
                    // Got to "undo" the soft delete status
                    actionType = ActionType.UnDeleteAndStore;
                    snapshot.Deleted = false;
                    break;
            }
        }

        return (snapshot, actionType);
    }

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/explicit_code_for_aggregation_logic.cs#L636-L705' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_custom_aggregate_with_start_and_stop' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


