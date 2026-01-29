# Composite or Chained Projections <Badge type="tip" text="8.18" />

::: info
This feature was introduced in Marten 8.18 in response to feedback from several [JasperFx Software](https://jasperfx.net)
clients who needed to efficiently create projections that effectively made de-normalized views across multiple
stream types. Expect this feature to grow in capability as we get more feedback about its usage.
:::

::: tip
"Composite" projections are automatically running with a `ProjectionLifecycle.Async` lifecycle.
:::

Here are a handful of scenarios that Marten users have hit over the years:

* Wanting to use the build products of Projection 1 as an input to Projection 2. You can do that today by running Projection 1 as `Inline` and Projection 2 as `Async`, but that's imperfect and sensitive to timing. Plus, you might not have *wanted* to run the first projection `Inline`.
* Needing to create a de-normalized projection view that incorporates data from several other projections and completely different types of event streams, but that previously required quite a bit of duplicated logic between projections
* Looking for ways to improve the throughput of asynchronous projections by doing more batching of event fetching and projection updates by trying to run multiple projections together

To meet these somewhat common needs more easily, Marten has introduced the concept of a "composite" projection where Marten
is able to run multiple projections together and possibly divided into multiple, sequential stages. This provides some
potential benefits by enabling you to safely use the build products of one projection as inputs to a second projection. Also,
if you have multiple projections using much of the same event data, you can wring out more runtime efficiency by building 
the projections together so your system is doing less work fetching events and able to make updates to the database with
fewer network round trips through bigger batches.

Let's jump right into an example using a "Telehealth" problem domain where patients of a medical service can request
and be matched up for medical appointments with medical providers for online appointments. 

That domain might have some plain Marten document storage
for reference data including:

* `Provider` -- representing a medical provider (Nurse? Physician? PA?) who fields appointments
* `Specialty` -- models a medical specialty 
* `Patient` -- personal information about patients who are requesting appointments in our system

Switching to event streams, we may be capturing events for:

* `Board` - events modeling a single, closely related group of appointments during a single day. Think of "Pediatrics in Austin, Texas for January 19th"
* `ProviderShift` - events modeling the activity of a single provider working in a single `Board` during a single day
* `Appointment` - events recording the progress of an appointment including requesting an appointment through the appointment being cancelled or completed

In this system, we need to have single stream "write model" projections for each of the three stream types. We also need
to have a rich view of each `Board` that combines all the common state of the active `Appointment` and `ProviderShift` streams
in that `Board` including the more static `Patient` and `Provider` information that can be used by the system to automate 
the assignment of providers to open patients (a real telehealth system would need to be able to match up the requirements of an appointment with the 
licensing, specialty, and location of the providers as well as "knowing" what providers are available or estimated to be available). We
probably also need to build a denormalized "query model" about all appointments that can be efficiently queried by our 
user interface on any of the elements of `Board`, `Appointment`, `Patient`, or `Provider`. 

What we really want is some way to efficiently utilize the upstream products and updates of the `Board`, `Appointment`, and `ProviderShift`
"write model" projections as inputs to what we'll call the `BoardSummary` and `AppointmentDetails` projections. We'll use the new
"composite projection" feature to run these projections together in two stages like this:

![Telehealth Projection](/images/telehealth-projection.png "Composite Projection")

Before we dive into each child projection, this is how we can 
set up the composite projection using the `StoreOptions` model
in Marten:~~~~

<!-- snippet: sample_defining_a_composite_projection -->
<a id='snippet-sample_defining_a_composite_projection'></a>
```cs
opts.Projections.CompositeProjectionFor("TeleHealth", projection =>
{
    projection.Add<ProviderShiftProjection>();
    projection.Add<AppointmentProjection>();
    projection.Snapshot<Board>();

    // 2nd stage projections
    projection.Add<AppointmentDetailsProjection>(2);
    projection.Add<BoardSummaryProjection>(2);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/Composites/multi_stage_projections.cs#L182-L195' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_defining_a_composite_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

First, let's just look at the simple `ProviderShiftProjection`:

<!-- snippet: sample_ProviderShiftProjection -->
<a id='snippet-sample_providershiftprojection'></a>
```cs
public class ProviderShiftProjection: SingleStreamProjection<ProviderShift, Guid>
{
    public ProviderShiftProjection()
    {
        // Make sure this is turned on!
        Options.CacheLimitPerTenant = 1000;
    }

    public override async Task EnrichEventsAsync(SliceGroup<ProviderShift, Guid> group, IQuerySession querySession, CancellationToken cancellation)
    {
        await group

            // First, let's declare what document type we're going to look up
            .EnrichWith<Provider>()

            // What event type or marker interface type or common abstract type
            // we could look for within each EventSlice that might reference
            // providers
            .ForEvent<ProviderJoined>()

            // Tell Marten how to find an identity to look up
            .ForEntityId(x => x.ProviderId)

            // And finally, execute the look up in one batched round trip,
            // and apply the matching data to each combination of EventSlice, event within that slice
            // that had a reference to a ProviderId, and the Provider
            .EnrichAsync((slice, e, provider) =>
            {
                // In this case we're swapping out the persisted event with the
                // enhanced event type before each event slice is then passed
                // in for updating the ProviderShift aggregates
                slice.ReplaceEvent(e, new EnhancedProviderJoined(e.Data.BoardId, provider));
            });
    }

    public override ProviderShift Evolve(ProviderShift snapshot, Guid id, IEvent e)
    {
        switch (e.Data)
        {
            case EnhancedProviderJoined joined:
                snapshot = new ProviderShift(joined.BoardId, joined.Provider)
                {
                    Provider = joined.Provider, Status = ProviderStatus.Ready
                };
                break;

            case ProviderReady:
                snapshot.Status = ProviderStatus.Ready;
                break;

            case AppointmentAssigned assigned:
                snapshot.Status = ProviderStatus.Assigned;
                snapshot.AppointmentId = assigned.AppointmentId;
                break;

            case ProviderPaused:
                snapshot.Status = ProviderStatus.Paused;
                snapshot.AppointmentId = null;
                break;

            case ChartingStarted charting:
                snapshot.Status = ProviderStatus.Charting;
                break;
        }

        return snapshot;
    }

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TeleHealth/ProviderShift.cs#L62-L142' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_providershiftprojection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Now, let's go downstream and look at the `AppointmentDetailsProjection` that will
ultimately need to use the build products of all three upstream projections:

<!-- snippet: sample_AppointmentDetailsProjection -->
<a id='snippet-sample_appointmentdetailsprojection'></a>
```cs
public class AppointmentDetailsProjection : MultiStreamProjection<AppointmentDetails, Guid>
{
    public AppointmentDetailsProjection()
    {
        Options.CacheLimitPerTenant = 1000;

        Identity<Updated<Appointment>>(x => x.Entity.Id);
        Identity<IEvent<ProviderAssigned>>(x => x.StreamId);
        Identity<IEvent<AppointmentRouted>>(x => x.StreamId);

        // This is a synthetic event published from upstream projections to identify
        // which projected Appointment documents were deleted as part of the current event range
        // so we can keep this richer model mirroring the simpler Appointment projection
        Identity<ProjectionDeleted<Appointment, Guid>>(x => x.Identity);
    }

    public override async Task EnrichEventsAsync(SliceGroup<AppointmentDetails, Guid> group, IQuerySession querySession, CancellationToken cancellation)
    {
        // Look up and apply specialty information from the document store
        // Specialty is just reference data stored as a document in Marten
        await group
            .EnrichWith<Specialty>()
            .ForEvent<Updated<Appointment>>()
            .ForEntityId(x => x.Entity.Requirement.SpecialtyCode)
            .AddReferences();

        // Also reference data (for now)
        await group
            .EnrichWith<Patient>()
            .ForEvent<Updated<Appointment>>()
            .ForEntityId(x => x.Entity.PatientId)
            .AddReferences();

        // Look up and apply provider information
        await group
            .EnrichWith<Provider>()
            .ForEvent<ProviderAssigned>()
            .ForEntityId(x => x.ProviderId)
            .AddReferences();

        // Look up and apply Board information that matches the events being
        // projected
        await group
            .EnrichWith<Board>()
            .ForEvent<AppointmentRouted>()
            .ForEntityId(x => x.BoardId)
            .AddReferences();

    }

    public override AppointmentDetails Evolve(AppointmentDetails snapshot, Guid id, IEvent e)
    {
        switch (e.Data)
        {
            case AppointmentRequested requested:
                snapshot ??= new AppointmentDetails(e.StreamId);
                snapshot.SpecialtyCode = requested.SpecialtyCode;
                snapshot.PatientId = requested.PatientId;
                break;

            // This is an upstream projection. Triggering off of a synthetic
            // event that Marten publishes from the early stage
            // to this projection running in a secondary stage
            case Updated<Appointment> updated:
                snapshot ??= new AppointmentDetails(updated.Entity.Id);
                snapshot.Status = updated.Entity.Status;
                snapshot.EstimatedTime = updated.Entity.EstimatedTime;
                snapshot.SpecialtyCode = updated.Entity.SpecialtyCode;
                break;

            case References<Patient> patient:
                snapshot.PatientFirstName = patient.Entity.FirstName;
                snapshot.PatientLastName = patient.Entity.LastName;
                break;

            case References<Specialty> specialty:
                snapshot.SpecialtyCode = specialty.Entity.Code;
                snapshot.SpecialtyDescription = specialty.Entity.Description;
                break;

            case References<Provider> provider:
                snapshot.ProviderId = provider.Entity.Id;
                snapshot.ProviderFirstName = provider.Entity.FirstName;
                snapshot.ProviderLastName = provider.Entity.LastName;
                break;

            case References<Board> board:
                snapshot.BoardName = board.Entity.Name;
                snapshot.BoardId = board.Entity.Id;
                break;

            // The matching projection for Appointment was deleted
            // so we'll delete this enriched projection as well
            // ProjectionDeleted<TDoc> is a synthetic event that Marten
            // itself publishes from the upstream projections and available
            // to downstream projections
            case ProjectionDeleted<Appointment>:
                return null;
        }

        return snapshot;
    }

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TeleHealth/AppointmentDetailsProjection.cs#L14-L128' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_appointmentdetailsprojection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
The new `Updated<T>` synthetic event that we're using to communicate updates between projections can also be used within
`Apply()`, `Create`, or `ShouldDelete` methods as well.

There is also a corresponding `ProjectionDeleted<TDoc, TId>` synthetic event that will communicate updates between projections
and can also be used with `Apply()`, `Create`, or `ShouldDelete` methods.

The `ProjectionDeleted<TDoc,TId>` also implements some simpler interfaces `ProjectionDeleted<TDoc>` and `DeletedIdentity<TId>` that are available
just as conveniences to avoid the proliferation of ugly generics in your code.
:::

And also the definition for the downstream `BoardSummary` view:

<!-- snippet: sample_BoardSummaryProjection -->
<a id='snippet-sample_boardsummaryprojection'></a>
```cs
public class BoardSummaryProjection: MultiStreamProjection<BoardSummary, Guid>
{
    public BoardSummaryProjection()
    {
        Options.CacheLimitPerTenant = 100;

        Identity<Updated<Appointment>>(x => x.Entity.BoardId ?? Guid.Empty);
        Identity<Updated<Board>>(x => x.Entity.Id);
        Identity<Updated<ProviderShift>>(x => x.Entity.BoardId);
    }

    public override Task EnrichEventsAsync(SliceGroup<BoardSummary, Guid> group, IQuerySession querySession, CancellationToken cancellation)
    {
        return group.ReferencePeerView<Board>();
    }

    public override (BoardSummary, ActionType) DetermineAction(BoardSummary snapshot, Guid identity, IReadOnlyList<IEvent> events)
    {
        snapshot ??= new BoardSummary { Id = identity };
        if (events.TryFindReference<Board>(out var board))
        {
            snapshot.Board = board;
        }

        var shifts = events.AllReferenced<ProviderShift>().ToArray();
        foreach (var providerShift in shifts)
        {
            snapshot.ActiveProviders[providerShift.ProviderId] = providerShift;

            if (providerShift.AppointmentId.HasValue)
            {
                snapshot.Unassigned.Remove(providerShift.ProviderId);
            }
        }

        foreach (var appointment in events.AllReferenced<Appointment>())
        {
            if (appointment.ProviderId == null)
            {
                snapshot.Unassigned[appointment.Id] = appointment;
                snapshot.Assigned.Remove(appointment.Id);
            }
            else
            {
                snapshot.Unassigned.Remove(appointment.Id);
                var shift = shifts.FirstOrDefault(x => x.Id == appointment.ProviderId.Value);

                snapshot.Assigned[appointment.Id] = new AssignedAppointment(appointment, shift?.Provider);
            }
        }

        return (snapshot, ActionType.Store);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TeleHealth/BoardSummary.cs#L30-L87' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_boardsummaryprojection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note the usage of the `Updated<T>` event types that the downstream projections are using in their
`Evolve` or `DetermineAction` methods. That is a synthetic event added by Marten to communicate to the downstream
projections what projected documents were updated for the current event range. These events are carrying the latest
snapshot data for the current event range so the downstream projections can just use the build products without making
any additional fetches. It also guarantees that the downstream projections are seeing the exact correct upstream projection
data for that point of the event sequencing.

Moreover, the composite "telehealth" projection is reading the event range *once* for all five constituent projections,
and also applying the updates for all five projections at one time to guarantee consistency.

## Things to Know About Composite Projections

* Composite projections can include any possible kind of projection including aggregations or event projections or flat table projections
* Composite projections can only run asynchronously
* In the event progression table, you will see rows for both the parent projection and all constituent projections -- but they should never be different values. This is so that you can later de-couple the projections and also to...
* The child parts of composite projections play nicely with `FetchForWriting`, `FetchLatest`, and `QueryForNonStaleData<T>()` operators
* You can apply versions to the composite projection itself, and that will overwrite the version of each child projection within the composite
* You can use as many stages as you wish, but we're not sure why you would need to use more than 2 or 3
* Side effects will work with composite projections, but they will be executed after the entire batch of changes are made for all constituent projections
* If you rebuild a composite projection, you will have to rebuild all constituent projections

Any other questions? You might have to reach out to the Marten team via Discord.
