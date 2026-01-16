# Enriching Events <Badge type="tip" text="8.11" />

::: tip
We added a newer recipe for more declarative and efficient event enrichment in Marten 8.18. 
Please see the later examples in the page too.
:::

So here’s a common scenario when building a system using Event Sourcing with Marten:

1. Some of the data in your system is just reference data stored as plain old Marten documents. 
   Something like user data (like I’ll use in just a bit), company data, or some other kind of static reference data 
   that doesn’t justify the usage of Event Sourcing. Or maybe you have some data that is event 
   sourced, but it’s very static data otherwise and you can essentially treat the projected documents as just documents. 
2. You have workflows modeled with event sourcing and you want some of the projections from those 
   events to also include information from the reference data documents

As an example, let’s say that your application has some reference information about system users saved in this document type 
(from the Marten testing suite):

```csharp
public class User
{
    public User()
    {
        Id = Guid.NewGuid();
    }
 
    public List<Friend> Friends { get; set; }
 
    public string[] Roles { get; set; }
    public Guid Id { get; set; }
    public string UserName { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string FullName => $"{FirstName} {LastName}";
}
```

And you also have events for some kind of `UserTask` aggregate that manages the workflow 
of some kind of work tracking. You might have some events like this:

```csharp
public record TaskLogged(string Name);
public record TaskStarted;
public record TaskFinished;
 
public class UserAssigned
{
    public Guid UserId { get; set; }
 
    // You don't *have* to do this with a mutable
    // property, but it is *an* easy way to pull this off
    public User? User { get; set; }
}
```

In a “query model” view of the event data, you’d love to be able to show the full, 
human readable User information about the user’s full name right into the projected document:

```csharp
public class UserTask
{
    public Guid Id { get; set; }
    public bool HasStarted { get; set; }
    public bool HasCompleted { get; set; }
    public Guid? UserId { get; set; }
 
    // This would be sourced from the User
    // documents
    public string UserFullName { get; set; }
}
```

In the projection for `UserTask`, you can always reach out to Marten in an adhoc way 
to grab the right User documents like this possible code in the projection definition 
for `UserTask`:

```csharp
// We're just gonna go look up the user we need right here and now!
public async Task Apply(UserAssigned assigned, IQuerySession session, UserTask snapshot)
{
    var user = await session.LoadAsync<User>(assigned.UserId);
    snapshot.UserFullName = user.FullName;
}
```

The ability to just pull in `IQuerySession` and go look up whatever data you need as you need it is certainly powerful, but hold on a bit, because what if:

1. You’re running the projection for `UserTask` asynchronously using Marten’s [async daemon](/events/projections/async-daemon) where it updates potentially hundreds of `UserTask` documents a the same time?
2. You expect the `UserAssigned` events to be quite common, so there’s a lot of potential `User` lookups to process the projection
3. You are quite aware that the code above could easily turn into an [N+1 Query Problem](https://medium.com/databases-in-simple-words/the-n-1-database-query-problem-a-simple-explanation-and-solutions-ef11751aef8a) that won’t be helpful at all for your system’s performance. And if you weren’t aware of that before, please be so now!

Instead of the *N+1 Query Problem* you could easily get from doing the `User` lookup one single event at a time, what if instead we were able to batch up the calls to lookup all the necessary `User` information for a batch of `UserTask` data being updated by the async daemon?

That's where the `EnrichEventsAsync()` template method can come into play on your aggregation projections
as a way of wringing more performance and scalability out of your Marten usage! Let’s build a single stream projection 
for the `UserTask` aggregate type shown up above that batches the `User` lookup:

<!-- snippet: snippet_UserTaskProjection -->
<a id='snippet-snippet_usertaskprojection'></a>
```cs
public class UserTaskProjection: SingleStreamProjection<UserTask, Guid>
{
    // This is where you have a hook to "enrich" event data *after* slicing,
    // but before processing
    public override async Task EnrichEventsAsync(
        SliceGroup<UserTask, Guid> group,
        IQuerySession querySession,
        CancellationToken cancellation)
    {
        // First, let's find all the events that need a little bit of data lookup
        var assigned = group
            .Slices
            .SelectMany(x => x.Events().OfType<IEvent<UserAssigned>>())
            .ToArray();

        // Don't bother doing anything else if there are no matching events
        if (!assigned.Any()) return;

        var userIds = assigned.Select(x => x.Data.UserId)
            // Hey, watch this. Marten is going to helpfully sort this out for you anyway
            // but we're still going to make it a touch easier on PostgreSQL by
            // weeding out multiple ids
            .Distinct().ToArray();
        var users = await querySession.LoadManyAsync<User>(cancellation, userIds);

        // Just a convenience
        var lookups = users.ToDictionary(x => x.Id);
        foreach (var e in assigned)
        {
            if (lookups.TryGetValue(e.Data.UserId, out var user))
            {
                e.Data.User = user;
            }
        }
    }

    // This is the Marten 8 way of just writing explicit code in your projection
    public override UserTask Evolve(UserTask snapshot, Guid id, IEvent e)
    {
        snapshot ??= new UserTask { Id = id };
        switch (e.Data)
        {
            case UserAssigned assigned:
                snapshot.UserId = assigned?.User.Id;
                snapshot.UserFullName = assigned?.User.FullName;
                break;

            case TaskStarted:
                snapshot.HasStarted = true;
                break;

            case TaskFinished:
                snapshot.HasCompleted = true;
                break;
        }

        return snapshot;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/when_enriching_events_for_aggregation_projections.cs#L85-L147' title='Snippet source file'>snippet source</a> | <a href='#snippet-snippet_usertaskprojection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Focus please on the `EnrichEventsAsync()` method above. That’s lets you define a step in asynchronous projection 
running to potentially do batched data lookups immediately after Marten has “sliced” 
and grouped a batch of events by each aggregate identity that is about to be updated, 
but before the actual updates are made to any of the `UserTask` snapshot documents.

In the code above, we’re looking for all the unique user ids that are referenced by any `UserAssigned` events in 
this batch of events, and making one single call to Marten to fetch the matching User documents. 
Lastly, we’re looping around on the `AgentAssigned` objects and actually “enriching” the 
events by setting a User property on them with the data we just looked up.

A couple other things:

It might not be terribly obvious, but you could still use immutable types for your event data 
and “just” quietly swap out single event objects within the `EventSlice` groupings as well.

You can also do “event enrichment” in any kind of custom grouping within `MultiStreamProjection` types without 
this new hook method, but we needed this to have an easy recipe at least for 
`SingleStreamProjection` classes. You might find this hook easier to use than doing database 
lookups in custom grouping anyway.

## Declarative Enrichment <Badge type="tip" text="8.18" />

As part of the work on [composite or chained projections](/events/projections/composite) in Marten 8.18,
we were also able to add some new, hopefully easier to use recipes for more declarative event
enrichment. 

First, for a little background. In the testing suite, we have a fake "TeleHealth" problem domain coded
up that has the concept of a `ProviderShift` event stream that refers to the work of a single health
care provider (Doctor, Nurse Practitioner, P.A., etc.) during a single day. The `Provider` data (personal information, licensing) is assumed
to be relatively static, so that information is just stored as a Marten document.

The first event in a `ProviderShift` stream might be this immutable type:

<!-- snippet: sample_ProviderJoined -->
<a id='snippet-sample_providerjoined'></a>
```cs
public record ProviderJoined(Guid BoardId, Guid ProviderId);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TeleHealth/ProviderShift.cs#L43-L47' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_providerjoined' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In the projection for these streams to a `ProviderShift` document we'd really like to read some of the basic `Provider` information
like this:

<!-- snippet: sample_ProviderShift -->
<a id='snippet-sample_providershift'></a>
```cs
public class ProviderShift(Guid boardId, Provider provider)
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public Guid BoardId { get; private set; } = boardId;
    public Guid ProviderId => Provider.Id;
    public ProviderStatus Status { get; set; } = ProviderStatus.Paused;
    public string Name { get; init; }
    public Guid? AppointmentId { get; set; }

    // I was admittedly lazy in the testing, so I just
    // completely embedded the Provider document directly
    // in the ProviderShift for easier querying later
    public Provider Provider { get; set; } = provider;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TeleHealth/ProviderShift.cs#L11-L29' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_providershift' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: info
Just to explain some async daemon nomenclature:

"Range" or "Page" -- the daemon is processing a range of events read in from the database at a time. For example, events with a `Sequence` of 1,000 to 2,000
"Slice" -- in any kind of aggregation projection, the daemon is "slicing" or "grouping" the raw range of events into an `EventSlice` of the events from that range that
apply to a single aggregate identity. In the case of a single stream projection, a "slice" is all the events in a range or page that have
the same stream id
:::

Inside the projection class for `ProviderShift`, we're going to implement the `EnrichEventsAsync()` such that
we look up all the `Provider` documents that are referenced by `ProviderJoined` events in the current range of events
that the async daemon is processing, and try to swap out the `ProviderJoined` events in each slice for a copy
of this enhanced event type:

<!-- snippet: sample_EnhancedProviderJoined -->
<a id='snippet-sample_enhancedproviderjoined'></a>
```cs
public record EnhancedProviderJoined(Guid BoardId, Provider Provider);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TeleHealth/ProviderShift.cs#L49-L53' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_enhancedproviderjoined' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Here's the enrichment code that looks up a `Provider` for each `ProviderJoined` event, and swaps in a fatter
`ProviderJoinedEnhanced` event:

<!-- snippet: sample_ProviderShift_EnrichEventsAsync -->
<a id='snippet-sample_providershift_enricheventsasync'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TeleHealth/ProviderShift.cs#L72-L101' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_providershift_enricheventsasync' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In the projection itself, we work on the enhanced event type like this:

<!-- snippet: sample_ProviderShift_Evolve -->
<a id='snippet-sample_providershift_evolve'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TeleHealth/ProviderShift.cs#L103-L138' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_providershift_evolve' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Moving on to another example from the "TeleHealth" problem domain, there's a pair of related concepts:

1. An `Appointment` event stream
2. A `Board` event stream that reflects a group of related appointments and provider shifts during a single day. Think "Pediatrics Appointments for Austin, TX" as a `Board` (I worked on a TeleHealth system during the worst of the COVID pandemic, and also spent quite a bit of time taking small children to the pediatrician for every little bug that came through their school for a while. Hence, this example in the code)

In the TeleHealth system, let's say that we have a query model projection to support our front end
that is a simple denormalized view of an active `Appointment`, the `Board` that the `Appointment` belongs to,
and even the `Provider` assigned to that active or scheduled `Provider`. 

When we execute and build up this projection, we need the related `Provider` and `Board` documents
to build up our projected `AppointmentDetails` document. Part of the `EnrichEventsAsync()` method
for this projection includes these two lookups:

<!-- snippet: sample_using_forevent_addreferences -->
<a id='snippet-sample_using_forevent_addreferences'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TeleHealth/AppointmentDetailsProjection.cs#L44-L61' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_forevent_addreferences' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

What this does is data lookup for all the unique `Provider` and `Board` documents that match
any of the events in the current event range, and adds a `References<T>` event to each event slic
for matching `Provider` or `Board` documents.

In the `Evolve()` method for the projection, we can look for those "synthetic events" like this:

<!-- snippet: sample_AppointmentDetails_Evolve -->
<a id='snippet-sample_appointmentdetails_evolve'></a>
```cs
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
    }

    return snapshot;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TeleHealth/AppointmentDetailsProjection.cs#L65-L112' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_appointmentdetails_evolve' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->











