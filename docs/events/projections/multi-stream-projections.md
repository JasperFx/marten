# Multi-Stream Projections

::: tip
We have replaced the earlier nomenclature of "ViewProjection" and renamed this concept as
`MultiStreamProjection` to hopefully be more intention revealing.
:::

::: warning
**Multi-Stream Projections are registered by default as async.** This is different from Single-Stream Projections. We recommend it as safe default because their processing may be more resource-demanding than single-stream projections. They may need to process more events and update more read models. Still, you can change that setting and register them synchronously if you're aware of that tradeoff.

**Registering projection as async means that it requires running the
[Async Daemon](/events/projections/async-daemon) as hosted service.**

If you have Multi-Stream Projections registered as async and Async Daemon is not running, then projection won't be processed. Marten will issue a warning in logs during startup in case of such a mismatch.
:::

Multi stream projections are designed to handle multi-stream projections where a view is aggregated over events between streams. The `MultiStreamProjection<TDoc, TId>`
base class is a subclass of the simpler [Single Stream Projection](/events/projections/aggregate-projections) and supports all the same method conventions and inline event handling, but allows
the user to specify how events apply to aggregated views in ways besides the simple aggregation by stream model.

For simple event to aggregate groupings, you can use the:

- `Identity<TEvent>(Func<TEvent, TId> func)` method to assign an incoming event to a single aggregate by the aggregate id. Do note that this method works
  with common base classes or common interfaces so you don't have to specify this for every single event type
- `Identities<TEvent>(Func<TEvent, IReadOnlyList<TId>> identitiesFunc)` method to assign an incoming event to multiple aggregates
- For grouping rules that fall outside the simpler `Identity()` or `Identities()` methods, supply a custom `IAggregateGrouper` that will sort events into aggregate groups of events by aggregate id
- For advanced usages, supply a custom `IEventSlicer` that let's you write your own mechanism to divide an incoming segment of events to aggregated view documents

It's important to note that the first three options (`Identity()`, `Identities()`, and custom `IAggregateGrouping`) are completely additive, while
using a custom `IEventSlicer` is a complete replacement for the first three approaches and cannot be used simultaneously.

The last two mechanisms will allow you to use additional information in the underlying Marten database.

Jumping right into an example, having defined events and views as:

<!-- snippet: sample_view-projection-test-classes -->
<a id='snippet-sample_view-projection-test-classes'></a>
```cs
public interface IUserEvent
{
    Guid UserId { get; }
}

// License events
public class LicenseCreated
{
    public Guid LicenseId { get; }

    public string Name { get; }

    public LicenseCreated(Guid licenseId, string name)
    {
        LicenseId = licenseId;
        Name = name;
    }
}

public class LicenseFeatureToggled
{
    public Guid LicenseId { get; }

    public string FeatureToggleName { get; }

    public LicenseFeatureToggled(Guid licenseId, string featureToggleName)
    {
        LicenseId = licenseId;
        FeatureToggleName = featureToggleName;
    }
}

public class LicenseFeatureToggledOff
{
    public Guid LicenseId { get; }

    public string FeatureToggleName { get; }

    public LicenseFeatureToggledOff(Guid licenseId, string featureToggleName)
    {
        LicenseId = licenseId;
        FeatureToggleName = featureToggleName;
    }
}

// User Groups events

public class UserGroupCreated
{
    public Guid GroupId { get; }

    public string Name { get; }

    public UserGroupCreated(Guid groupId, string name)
    {
        GroupId = groupId;
        Name = name;
    }
}

public class SingleUserAssignedToGroup : IUserEvent
{
    public Guid GroupId { get; }

    public Guid UserId { get; }

    public SingleUserAssignedToGroup(Guid groupId, Guid userId)
    {
        GroupId = groupId;
        UserId = userId;
    }
}

public class MultipleUsersAssignedToGroup
{
    public Guid GroupId { get; }

    public List<Guid> UserIds { get; }

    public MultipleUsersAssignedToGroup(Guid groupId, List<Guid> userIds)
    {
        GroupId = groupId;
        UserIds = userIds;
    }
}

// User Events
public class UserRegistered : IUserEvent
{
    public Guid UserId { get; }

    public string Email { get; }

    public UserRegistered(Guid userId, string email)
    {
        UserId = userId;
        Email = email;
    }
}

public class UserLicenseAssigned
{
    public Guid UserId { get; }

    public Guid LicenseId { get; }

    public UserLicenseAssigned(Guid userId, Guid licenseId)
    {
        UserId = userId;
        LicenseId = licenseId;
    }
}

public class UserFeatureToggles
{
    public Guid Id { get; set; }

    public Guid LicenseId { get; set; }

    public List<string> FeatureToggles { get; set; } = new();
}

public class UserGroupsAssignment
{
    public Guid Id { get; set; }

    public List<Guid> Groups { get; set; } = new();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/TestClasses.cs#L6-L138' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-test-classes' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Simple Event to Single Cross-Stream Projection

Here's a simple example of creating an aggregated view by user id:

<!-- snippet: sample_view-projection-simple -->
<a id='snippet-sample_view-projection-simple'></a>
```cs
public class UserGroupsAssignmentProjection: MultiStreamProjection<UserGroupsAssignment, Guid>
{
    public UserGroupsAssignmentProjection()
    {
        // This is just specifying the aggregate document id
        // per event type. This assumes that each event
        // applies to only one aggregated view document
        Identity<UserRegistered>(x => x.UserId);
        Identity<SingleUserAssignedToGroup>(x => x.UserId);
    }

    public void Apply(UserRegistered @event, UserGroupsAssignment view)
        => view.Id = @event.UserId;

    public void Apply(SingleUserAssignedToGroup @event, UserGroupsAssignment view)
        => view.Groups.Add(@event.GroupId);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/simple_multi_stream_projection.cs#L10-L29' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-simple' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note that the primary difference between this and `SingleStreamProjection<T>` is the calls to `Identity<TEvent>()` to specify how the events are grouped
into separate aggregates across streams. We can also do the equivalent of the code above by using a common interface `IUserEvent` on the event types
we care about and use this:

<!-- snippet: sample_view-projection-simple-2 -->
<a id='snippet-sample_view-projection-simple-2'></a>
```cs
public class UserGroupsAssignmentProjection2: MultiStreamProjection<UserGroupsAssignment, Guid>
{
    public UserGroupsAssignmentProjection2()
    {
        // This is just specifying the aggregate document id
        // per event type. This assumes that each event
        // applies to only one aggregated view document

        // The easiest possible way to do this is to use
        // a common interface or base type, and specify
        // the identity rule on that common type
        Identity<IUserEvent>(x => x.UserId);
    }

    public void Apply(UserRegistered @event, UserGroupsAssignment view)
        => view.Id = @event.UserId;

    public void Apply(SingleUserAssignedToGroup @event, UserGroupsAssignment view)
        => view.Groups.Add(@event.GroupId);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/simple_multi_stream_projection.cs#L31-L53' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-simple-2' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As of Marten V7, you can also use `IEvent` metadata as part of creating the identity rules as shown in this example:

<!-- snippet: sample_using_ievent_for_document_identity_in_projections -->
<a id='snippet-sample_using_ievent_for_document_identity_in_projections'></a>
```cs
public class CustomerInsightsProjection : MultiStreamProjection<CustomerInsightsResponse, string>
{

    public CustomerInsightsProjection()
    {
        Identity<IEvent<CustomerCreated>>(x => DateOnly.FromDateTime(x.Timestamp.Date).ToString(CultureInfo.InvariantCulture));
        Identity<IEvent<CustomerDeleted>>(x => DateOnly.FromDateTime(x.Timestamp.Date).ToString(CultureInfo.InvariantCulture));
    }

    public CustomerInsightsResponse Create(IEvent<CustomerCreated> @event)
        => new(@event.Timestamp.Date.ToString(CultureInfo.InvariantCulture), DateOnly.FromDateTime(@event.Timestamp.DateTime), 1);

    public CustomerInsightsResponse Apply(IEvent<CustomerCreated> @event, CustomerInsightsResponse current)
        => current with { NewCustomers = current.NewCustomers + 1 };

    public CustomerInsightsResponse Apply(IEvent<CustomerDeleted> @event, CustomerInsightsResponse current)
        => current with { NewCustomers = current.NewCustomers - 1 };
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Bugs/Bug_2883_ievent_not_working_as_identity_source_in_multistream_projections.cs#L78-L98' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_ievent_for_document_identity_in_projections' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Simple Example of Events Updating Multiple Views

In the following projection, we apply the `MultipleUsersAssignedToGroup` event to multiple
different `UserGroupsAssignment` projected documents with the usage of the `Identities()` method
shown below:

<!-- snippet: sample_view-projection-simple-with-one-to-many -->
<a id='snippet-sample_view-projection-simple-with-one-to-many'></a>
```cs
public class UserGroupsAssignmentProjection: MultiStreamProjection<UserGroupsAssignment, Guid>
{
    public UserGroupsAssignmentProjection()
    {
        Identity<UserRegistered>(x => x.UserId);

        // You can now use IEvent<T> as well as declaring this against the core event type
        Identities<IEvent<MultipleUsersAssignedToGroup>>(x => x.Data.UserIds);
    }

    public void Apply(UserRegistered @event, UserGroupsAssignment view)
    {
        view.Id = @event.UserId;
    }

    public void Apply(MultipleUsersAssignedToGroup @event, UserGroupsAssignment view)
    {
        view.Groups.Add(@event.GroupId);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/simple_multi_stream_projection_wih_one_to_many.cs#L12-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-simple-with-one-to-many' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## View Projection with Custom Grouper

::: warning
If your grouping logic requires you to access the aggregate view itself, `ViewProjection` **will not function correctly**
because of operation ordering (grouping happens in parallel to building projection views as a performance optimization). If
your grouping logic does require loading the actual aggregate documents, you need to author a custom implementation of the raw
`IProjection` interface.
:::

As simpler mechanism to group events to aggregate documents is to supply a custom `IAggregatorGrouper<TId>` as shown below:

<!-- snippet: sample_view-projection-custom-grouper-with-querysession -->
<a id='snippet-sample_view-projection-custom-grouper-with-querysession'></a>
```cs
public class LicenseFeatureToggledEventGrouper: IAggregateGrouper<Guid>
{
    public async Task Group(IQuerySession session, IEnumerable<IEvent> events, ITenantSliceGroup<Guid> grouping)
    {
        var licenseFeatureTogglesEvents = events
            .OfType<IEvent<LicenseFeatureToggled>>()
            .ToList();

        if (!licenseFeatureTogglesEvents.Any())
        {
            return;
        }

        // TODO -- let's build more samples first, but see if there's a useful
        // pattern for the next 3/4 operations later
        var licenseIds = licenseFeatureTogglesEvents
            .Select(e => e.Data.LicenseId)
            .ToList();

        var result = await session.Query<UserFeatureToggles>()
            .Where(x => licenseIds.Contains(x.LicenseId))
            .Select(x => new {x.Id, x.LicenseId})
            .ToListAsync();

        var streamIds = (IDictionary<Guid, List<Guid>>)result.GroupBy(ks => ks.LicenseId, vs => vs.Id)
            .ToDictionary(ks => ks.Key, vs => vs.ToList());

        grouping.AddEvents<LicenseFeatureToggled>(e => streamIds[e.LicenseId], licenseFeatureTogglesEvents);
    }
}

// projection with documentsession
public class UserFeatureTogglesProjection: MultiStreamProjection<UserFeatureToggles, Guid>
{
    public UserFeatureTogglesProjection()
    {
        Identity<UserRegistered>(@event => @event.UserId);
        Identity<UserLicenseAssigned>(@event => @event.UserId);

        CustomGrouping(new LicenseFeatureToggledEventGrouper());
    }

    public void Apply(UserRegistered @event, UserFeatureToggles view)
    {
        view.Id = @event.UserId;
    }

    public void Apply(UserLicenseAssigned @event, UserFeatureToggles view)
    {
        view.LicenseId = @event.LicenseId;
    }

    public void Apply(LicenseFeatureToggled @event, UserFeatureToggles view)
    {
        view.FeatureToggles.Add(@event.FeatureToggleName);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/custom_grouper_with_document_session.cs#L15-L74' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-custom-grouper-with-querysession' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## View Projection with Custom Slicer

::: tip
Note that if you use a custom slicer, you'll be responsible for organizing events and documents by tenant id if your
document view type should be multi-tenanted.
:::

If `Identity()` or `Identities()` is too limiting for your event aggregation rules, you can drop down and implement your
own `IEventSlicer` that can split and assign events to any number of aggregated document views. Below is an example:

<!-- snippet: sample_view-projection-custom-slicer -->
<a id='snippet-sample_view-projection-custom-slicer'></a>
```cs
public class UserGroupsAssignmentProjection: MultiStreamProjection<UserGroupsAssignment, Guid>
{
    public class CustomSlicer: IEventSlicer<UserGroupsAssignment, Guid>
    {
        public ValueTask<IReadOnlyList<EventSlice<UserGroupsAssignment, Guid>>> SliceInlineActions(
            IQuerySession querySession, IEnumerable<StreamAction> streams)
        {
            var allEvents = streams.SelectMany(x => x.Events).ToList();
            var group = new TenantSliceGroup<UserGroupsAssignment, Guid>(Tenant.ForDatabase(querySession.Database));
            group.AddEvents<UserRegistered>(@event => @event.UserId, allEvents);
            group.AddEvents<MultipleUsersAssignedToGroup>(@event => @event.UserIds, allEvents);

            return new(group.Slices.ToList());
        }

        public ValueTask<IReadOnlyList<TenantSliceGroup<UserGroupsAssignment, Guid>>> SliceAsyncEvents(
            IQuerySession querySession, List<IEvent> events)
        {
            var group = new TenantSliceGroup<UserGroupsAssignment, Guid>(Tenant.ForDatabase(querySession.Database));
            group.AddEvents<UserRegistered>(@event => @event.UserId, events);
            group.AddEvents<MultipleUsersAssignedToGroup>(@event => @event.UserIds, events);

            return new(new List<TenantSliceGroup<UserGroupsAssignment, Guid>>{group});
        }
    }

    public UserGroupsAssignmentProjection()
    {
        CustomGrouping(new CustomSlicer());
    }

    public void Apply(UserRegistered @event, UserGroupsAssignment view)
    {
        view.Id = @event.UserId;
    }

    public void Apply(MultipleUsersAssignedToGroup @event, UserGroupsAssignment view)
    {
        view.Groups.Add(@event.GroupId);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/custom_slicer.cs#L16-L59' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-custom-slicer' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Rollup by Tenant Id <Badge type="tip" text="7.15.0" />

::: info
This feature was built specifically for a [JasperFx](https://jasperfx.net) client who indeed had this use case in their system
:::

Let's say that your are using conjoined tenancy within your event storage, but want to create some kind of summarized roll up
document per tenant id in a projected document -- like maybe the number of open "accounts" or "issues" or "users."

To do that, there's a recipe for the "event slicing" in multi-stream projections with Marten to just group by the event's
tenant id and make that the identity of the projected document. That usage is shown below:

<!-- snippet: sample_rollup_projection_by_tenant_id -->
<a id='snippet-sample_rollup_projection_by_tenant_id'></a>
```cs
public class RollupProjection: MultiStreamProjection<Rollup, string>
{
    public RollupProjection()
    {
        // This opts into doing the event slicing by tenant id
        RollUpByTenant();
    }

    public void Apply(Rollup state, AEvent e) => state.ACount++;
    public void Apply(Rollup state, BEvent e) => state.BCount++;
}

public class Rollup
{
    [Identity]
    public string TenantId { get; set; }
    public int ACount { get; set; }
    public int BCount { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/rolling_up_by_tenant.cs#L55-L77' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rollup_projection_by_tenant_id' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do note that you'll probably also need this flag in your configuration:

```cs
// opts is a StoreOptions object
opts.Events.EnableGlobalProjectionsForConjoinedTenancy = true;
```

## Event "Fan Out" Rules

The `ViewProjection` also provides the ability to "fan out" child events from a parent event into the segment of events being used to
create an aggregated view. As an example, a `Travel` event we use in Marten testing contains a list of `Movement` objects:

<!-- snippet: sample_Travel_Movements -->
<a id='snippet-sample_travel_movements'></a>
```cs
public IList<Movement> Movements { get; set; } = new List<Movement>();
public List<Stop> Stops { get; set; } = new();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TestingSupport/Travel.cs#L40-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_travel_movements' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In a sample `ViewProjection`, we do a "fan out" of the `Travel.Movements` members into separate events being processed through the projection:

<!-- snippet: sample_showing_fanout_rules -->
<a id='snippet-sample_showing_fanout_rules'></a>
```cs
public class DayProjection: MultiStreamProjection<Day, int>
{
    public DayProjection()
    {
        // Tell the projection how to group the events
        // by Day document
        Identity<IDayEvent>(x => x.Day);

        // This just lets the projection work independently
        // on each Movement child of the Travel event
        // as if it were its own event
        FanOut<Travel, Movement>(x => x.Movements);

        // You can also access Event data
        FanOut<Travel, Stop>(x => x.Data.Stops);

        ProjectionName = "Day";

        // Opt into 2nd level caching of up to 100
        // most recently encountered aggregates as a
        // performance optimization
        CacheLimitPerTenant = 1000;

        // With large event stores of relatively small
        // event objects, moving this number up from the
        // default can greatly improve throughput and especially
        // improve projection rebuild times
        Options.BatchSize = 5000;
    }

    public void Apply(Day day, TripStarted e) => day.Started++;
    public void Apply(Day day, TripEnded e) => day.Ended++;

    public void Apply(Day day, Movement e)
    {
        switch (e.Direction)
        {
            case Direction.East:
                day.East += e.Distance;
                break;
            case Direction.North:
                day.North += e.Distance;
                break;
            case Direction.South:
                day.South += e.Distance;
                break;
            case Direction.West:
                day.West += e.Distance;
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Apply(Day day, Stop e) => day.Stops++;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/ViewProjectionTests.cs#L132-L192' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_showing_fanout_rules' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Using Custom Grouper with Fan Out Feature for Event Projections

In Marten, the `MultiStreamProjection` feature allows for complex transformations and aggregations of events. However, there might be scenarios where a single event in your domain carries information that is more suitable to be distributed across multiple instances of your projected read model. This is where the combination of a Custom Grouper and the Fan Out feature comes into play.

### The Scenario

Imagine you have a system where `EmployeeAllocated` events contain a list of allocations for specific days. The goal is to project this information into a monthly summary.

### Custom Projection with Custom Grouper

The `MonthlyAllocationProjection` class uses a custom grouper for this transformation. Here, `TransformsEvent<EmployeeAllocated>()` indicates that events of type `EmployeeAllocated` will be used even if there are no direct handlers for this event type in the projection.

<!-- snippet: sample_view-custom-grouper-with-transformation-projection -->
<a id='snippet-sample_view-custom-grouper-with-transformation-projection'></a>
```cs
public class MonthlyAllocationProjection: MultiStreamProjection<MonthlyAllocation, string>
{
    public MonthlyAllocationProjection()
    {
        CustomGrouping(new MonthlyAllocationGrouper());
        TransformsEvent<EmployeeAllocated>();
    }

    public void Apply(MonthlyAllocation allocation, EmployeeAllocatedInMonth @event)
    {
        allocation.EmployeeId = @event.EmployeeId;
        allocation.Month = @event.Month;

        var hours = @event
            .Allocations
            .Sum(x => x.Hours);

        allocation.Hours += hours;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/custom_grouper_with_events_transformation.cs#L40-L63' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-custom-grouper-with-transformation-projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Fan Out Using Custom Grouper

The custom grouper, `MonthlyAllocationGrouper`, is responsible for the logic of how events are grouped and fan-out.

<!-- snippet: sample_view-custom-grouper-with-transformation-grouper -->
<a id='snippet-sample_view-custom-grouper-with-transformation-grouper'></a>
```cs
public class MonthlyAllocationGrouper: IAggregateGrouper<string>
{
    public Task Group(
        IQuerySession session,
        IEnumerable<IEvent> events,
        ITenantSliceGroup<string> grouping
    )
    {
        var allocations = events
            .OfType<IEvent<EmployeeAllocated>>();

        var monthlyAllocations = allocations
            .SelectMany(@event =>
                @event.Data.Allocations.Select(
                    allocation => new
                    {
                        @event.Data.EmployeeId,
                        Allocation = allocation,
                        Month = allocation.Day.ToStartOfMonth(),
                        Source = @event
                    }
                )
            )
            .GroupBy(allocation =>
                new { allocation.EmployeeId, allocation.Month, allocation.Source }
            )
            .Select(monthlyAllocation =>
                new
                {

                    Key = $"{monthlyAllocation.Key.EmployeeId}|{monthlyAllocation.Key.Month:yyyy-MM-dd}",
                    Event = monthlyAllocation.Key.Source.WithData(
                        new EmployeeAllocatedInMonth(
                            monthlyAllocation.Key.EmployeeId,
                            monthlyAllocation.Key.Month,
                            monthlyAllocation.Select(a => a.Allocation).ToList())
                    )

                }
            );

        foreach (var monthlyAllocation in monthlyAllocations)
        {
            grouping.AddEvents(
                monthlyAllocation.Key,
                new[] { monthlyAllocation.Event }
            );
        }

        return Task.CompletedTask;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/custom_grouper_with_events_transformation.cs#L65-L122' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-custom-grouper-with-transformation-grouper' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Utilizing the `WithData()` Extension Method

Inside the `Group()` method, `WithData()` is employed to create a new type of event (`EmployeeAllocatedInMonth`) that still carries some attributes from the original event. This is essential for creating more specialized projections.

<!-- snippet: sample_view-custom-grouper-with-transformation-grouper-with-data -->
<a id='snippet-sample_view-custom-grouper-with-transformation-grouper-with-data'></a>
```cs
Key = $"{monthlyAllocation.Key.EmployeeId}|{monthlyAllocation.Key.Month:yyyy-MM-dd}",
Event = monthlyAllocation.Key.Source.WithData(
    new EmployeeAllocatedInMonth(
        monthlyAllocation.Key.EmployeeId,
        monthlyAllocation.Key.Month,
        monthlyAllocation.Select(a => a.Allocation).ToList())
)
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/custom_grouper_with_events_transformation.cs#L96-L106' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-custom-grouper-with-transformation-grouper-with-data' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Read also more in the [Event transformations, a tool to keep our processes loosely coupled](https://event-driven.io/en/event_transformations_and_loosely_coupling/?utm_source=marten_docs).
