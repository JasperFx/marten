# View Projections

View projections are designed to handle multistream projections where a view is aggregated over events between streams. The `ViewProjection<TDoc, TId>`
base class is a subclass of the simpler [Aggregate Projection](/guide/events/projections/aggregate-projections) and supports all the same method conventions and inline event handling, but allows
the user to specify how events apply to aggregated views in ways besides the simple aggregation by stream model.

For simple event to aggregate groupings, you can use the:

* `Identity<TEvent>(Func<TEvent, TId> func)` method to assign an incoming event to a single aggregate by the aggregate id. Do note that this method works
  with common base classes or common interfaces so you don't have to specify this for every single event type
* `Identities<TEvent>(Func<TEvent, IReadOnlyList<TId>> identitiesFunc)` method to assign an incoming event to multiple aggregates 
* For grouping rules that fall outside the simpler `Identity()` or `Identities()` methods, supply a custom `IAggregateGrouper` that will sort events into    
  aggregate groups of events by aggregate id
* For advanced usages, supply a custom `IEventSlicer` that let's you write your own mechanism to divide an incoming segment of events to 
  aggregated view documents

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Projections/ViewProjections/TestClasses.cs#L6-L138' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-test-classes' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Simple Event to Single View Projection

Here's a simple example of creating an aggregated view by user id:

<!-- snippet: sample_view-projection-simple -->
<a id='snippet-sample_view-projection-simple'></a>
```cs
public class UserGroupsAssignmentProjection: ViewProjection<UserGroupsAssignment, Guid>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Projections/ViewProjections/simple_multi_stream_projection.cs#L11-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-simple' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note that the primary difference between this and `AggregateProjection<T>` is the calls to `Identity<TEvent>()` to specify how the events are grouped
into separate aggregates across streams. We can also do the equivalent of the code above by using a common interface `IUserEvent` on the event types
we care about and use this:

<!-- snippet: sample_view-projection-simple-2 -->
<a id='snippet-sample_view-projection-simple-2'></a>
```cs
public class UserGroupsAssignmentProjection2: ViewProjection<UserGroupsAssignment, Guid>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Projections/ViewProjections/simple_multi_stream_projection.cs#L32-L54' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-simple-2' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## Simple Example of Events Updating Multiple Views

In the following projection, we apply the `MultipleUsersAssignedToGroup` event to multiple
different `UserGroupsAssignment` projected documents with the usage of the `Identities()` method
shown below:

<!-- snippet: sample_view-projection-simple-with-one-to-many -->
<a id='snippet-sample_view-projection-simple-with-one-to-many'></a>
```cs
public class UserGroupsAssignmentProjection: ViewProjection<UserGroupsAssignment, Guid>
{
    public UserGroupsAssignmentProjection()
    {
        Identity<UserRegistered>(x => x.UserId);
        Identities<MultipleUsersAssignedToGroup>(x => x.UserIds);
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Projections/ViewProjections/simple_multi_stream_projection_wih_one_to_many.cs#L12-L32' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-simple-with-one-to-many' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## View Projection with Custom Grouper

As simpler mechanism to group events to aggregate documents is to supply a custom `IAggregatorGrouper<TId>` as shown below:

<!-- snippet: sample_view-projection-custom-grouper-with-querysession -->
<a id='snippet-sample_view-projection-custom-grouper-with-querysession'></a>
```cs
public class LicenseFeatureToggledEventGrouper: IAggregateGrouper<Guid>
{
    public async Task Group(IQuerySession session, IEnumerable<IEvent> events, ITenantSliceGroup<Guid> grouping)
    {
        var licenceFeatureTogglesEvents = events
            .OfType<Event<LicenseFeatureToggled>>()
            .ToList();

        if (!licenceFeatureTogglesEvents.Any())
        {
            return;
        }

        // TODO -- let's build more samples first, but see if there's a useful
        // pattern for the next 3/4 operations later
        var licenceIds = licenceFeatureTogglesEvents
            .Select(e => e.Data.LicenseId)
            .ToList();

        var result = await session.Query<UserFeatureToggles>()
            .Where(x => licenceIds.Contains(x.LicenseId))
            .Select(x => new {x.Id, x.LicenseId})
            .ToListAsync();

        var streamIds = (IDictionary<Guid, List<Guid>>)result.GroupBy(ks => ks.LicenseId, vs => vs.Id)
            .ToDictionary(ks => ks.Key, vs => vs.ToList());

        grouping.AddEvents<LicenseFeatureToggled>(e => streamIds[e.LicenseId], licenceFeatureTogglesEvents);
    }
}

// projection with documentsession
public class UserFeatureTogglesProjection: ViewProjection<UserFeatureToggles, Guid>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Projections/ViewProjections/CustomGroupers/custom_grouper_with_document_session.cs#L15-L74' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-custom-grouper-with-querysession' title='Start of snippet'>anchor</a></sup>
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
public class UserGroupsAssignmentProjection: ViewProjection<UserGroupsAssignment, Guid>
{
    public class CustomSlicer: IEventSlicer<UserGroupsAssignment, Guid>
    {
        public ValueTask<IReadOnlyList<EventSlice<UserGroupsAssignment, Guid>>> SliceInlineActions(IQuerySession querySession, IEnumerable<StreamAction> streams, ITenancy tenancy)
        {
            var allEvents = streams.SelectMany(x => x.Events).ToList();
            var group = new TenantSliceGroup<UserGroupsAssignment, Guid>(tenancy.Default);
            group.AddEvents<UserRegistered>(@event => @event.UserId, allEvents);
            group.AddEvents<MultipleUsersAssignedToGroup>(@event => @event.UserIds, allEvents);

            return new(group.Slices.ToList());
        }

        public ValueTask<IReadOnlyList<TenantSliceGroup<UserGroupsAssignment, Guid>>> SliceAsyncEvents(IQuerySession querySession, List<IEvent> events, ITenancy tenancy)
        {
            var group = new TenantSliceGroup<UserGroupsAssignment, Guid>(tenancy.Default);
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Projections/ViewProjections/CustomGroupers/custom_slicer.cs#L16-L57' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-custom-slicer' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


