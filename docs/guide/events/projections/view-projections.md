# View Projections

View projections are designed to handled multistream projections.

Having defined events and views as:

<!-- snippet: sample_view-projection-test-classes -->
<a id='snippet-sample_view-projection-test-classes'></a>
```cs
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

public class SingleUserAssignedToGroup
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
public class UserRegistered
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

    public List<string> FeatureToggles { get; } = new();
}

public class UserGroupsAssignment
{
    public Guid Id { get; set; }

    public List<Guid> Groups { get; } = new();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Projections/ViewProjections/TestClasses.cs#L6-L132' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-test-classes' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Simple View Projection

<!-- snippet: sample_view-projection-simple -->
<a id='snippet-sample_view-projection-simple'></a>
```cs
public class UserGroupsAssignmentProjection: ViewProjection<UserGroupsAssignment, Guid>
{
    public UserGroupsAssignmentProjection()
    {
        Identity<UserRegistered>(x => x.UserId);
        Identity<SingleUserAssignedToGroup>(x => x.UserId);
    }

    public void Apply(UserRegistered @event, UserGroupsAssignment view)
    {
        view.Id = @event.UserId;
    }

    public void Apply(SingleUserAssignedToGroup @event, UserGroupsAssignment view)
    {
        view.Groups.Add(@event.GroupId);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Projections/ViewProjections/simple_multi_stream_projection.cs#L11-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-simple' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## Simple View Projection with event updating multiple views

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


## View Projection with custom slicer

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

## View Projection with custom grouper

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
