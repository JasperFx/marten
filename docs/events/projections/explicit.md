# Aggregation with Explicit Code

A major goal of Marten 8.0 was to improve our user's ability to utilize explicit code for defining projection
"evolve" logic. Sometimes because users disliked the conventional method approach, but also because the conventional
approach breaks down with complicated workflows like projection data that is soft-deleted, but maybe "un-deleted" in 
a reentrant workflow.

Inside of both `SingleStreamProjection<TDoc, TId>` and `MultiStreamProjection<TDoc, TId>`, you can choose to use explicit
code by overriding *one and only one* of these methods:

1. `Evolve` -- simple workflows where all you ever do is create, update, or delete projected views with just the event data
2. `EvolveAsync` -- `Evolve`, but with the ability to look up extra data with `IQuerySession`
3. `DetermineAction` -- more complex workflows where you might have reentrant states or utilize [soft deletes](/documents/deletes) for the persisted projection data
4. `DetermineActionAsync` -- `DetermineAction`, but with access to `IQuerySession` for extra data look ups during projection

The simplest and most common usage is to override the synchronous `Evolve` method that can update a projected document
through only the event data:

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

If your "evolve" step will require some data lookups or need to utilize any kind of asynchronous service, use
`EvolveAsync`:

<!-- snippet: sample_EvolveAsync -->
<a id='snippet-sample_evolveasync'></a>
```cs
public override ValueTask<LetterCounts> EvolveAsync(LetterCounts snapshot, Guid id, IQuerySession session, IEvent e, CancellationToken cancellation)
{
    // THIS projection isn't doing anything here, but you *could* use IQuerySession
    switch (e.Data)
    {
        case AEvent _:
            snapshot.ACount++;
            break;

        case BEvent _:
            snapshot.BCount++;
            break;

        case CEvent _:
            snapshot.CCount++;
            break;

        case DEvent _:
            snapshot.DCount++;
            break;
    }

    return new ValueTask<LetterCounts>(snapshot);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/stream_compacting.cs#L445-L472' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_evolveasync' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`Evolve` and `EvolveAsync` work by taking in a the current snapshot of the projected document and a single event, then
returning the updated version of the projected document -- or returning `null` to tell Marten to delete the projected
document. 

Now, if you need a more complicated workflow, use the `DetermineAction` or `DetermineActionAsync()` methods that let
you work with all the events and the incoming version of the projected document, and return to Marten a tuple
telling Marten *what* to do next and what the updated version of the projection should be.

Here's one example from the tests that was meant to test our ability to model reentrant workflows with soft-deleted projection
data (because users have absolutely wanted to do that over the years):

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

and another example:

<!-- snippet: sample_HardDeletedStartAndStopProjection -->
<a id='snippet-sample_harddeletedstartandstopprojection'></a>
```cs
public class HardDeletedStartAndStopProjection: SingleStreamProjection<HardDeletedStartAndStopAggregate, Guid>
{
    public HardDeletedStartAndStopProjection()
    {
        // This is an optional, but potentially important optimization
        // for the async daemon so that it sets up an allow list
        // of the event types that will be run through this projection
        IncludeType<Start>();
        IncludeType<End>();
        IncludeType<Restart>();
        IncludeType<Increment>();
    }

    public override (HardDeletedStartAndStopAggregate?, ActionType) DetermineAction(HardDeletedStartAndStopAggregate? snapshot, Guid identity,
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
                    snapshot = new HardDeletedStartAndStopAggregate
                    {
                        // Have to assign the identity ourselves
                        Id = identity
                    };
                    break;

                case Increment when snapshot is { }:
                    // Use explicit code to only apply this event
                    // if the snapshot already exists
                    snapshot.Increment();
                    break;

                case End when snapshot is {}:
                    actionType = ActionType.HardDelete;
                    snapshot = null;
                    break;

                case Restart when snapshot == null:
                    // Got to "undo" the soft delete status
                    actionType = ActionType.Store;
                    snapshot = new HardDeletedStartAndStopAggregate { Id = identity };
                    break;
            }
        }

        return (snapshot, actionType);
    }

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/aggregate_stream_returns_null_if_the_aggregate_is_null_at_that_point_in_stream.cs#L143-L206' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_harddeletedstartandstopprojection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
