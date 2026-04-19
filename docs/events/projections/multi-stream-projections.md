# Multi-Stream Projections

::: warning
**Multi-Stream Projections are registered by default as async.** We recommend it as safe default because under heavy load you can easily have contention between requests that effectively stomps over previous updates and leads to apparent "event skipping" and invalid results. Still, you can change that setting and register them synchronously if you're aware of that tradeoff.

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/simple_multi_stream_projection.cs#L11-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-simple' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/simple_multi_stream_projection.cs#L32-L54' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-simple-2' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Bugs/Bug_2883_ievent_not_working_as_identity_source_in_multistream_projections.cs#L70-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_ievent_for_document_identity_in_projections' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/simple_multi_stream_projection_wih_one_to_many.cs#L14-L37' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-simple-with-one-to-many' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Custom Grouper

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
    public async Task Group(IQuerySession session, IEnumerable<IEvent> events, IEventGrouping<Guid> grouping)
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/custom_grouper_with_document_session.cs#L18-L78' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-projection-custom-grouper-with-querysession' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Grouping events when the aggregate id is not on the event

It is very common that follow up events do not carry the id of the document you want to update. This can happen when:

- the initial event in a single stream still contains enough information to determine the multi stream group key
- later events in that same single stream no longer carry that information, even though they still need to update the same projected document

Below are three practical patterns to group events into the right `MultiStreamProjection` document without falling back to a full custom `IProjection`.

### Pattern 1, resolve the aggregate id through an inline lookup projection

Use this when events have a stable single stream identifier, but do not carry the multi stream aggregate id.

The idea is:

1. An inline single stream projection maintains a lookup document, or a flat table, that maps from the single stream id to the aggregate id
2. A custom `IAggregateGrouper` batches lookups for a range of events, then assigns those events to the right aggregate id

::: tip
Register the lookup projection as inline so the mapping is available when the async multi stream projection is grouping events.
:::

#### Example

Events are produced per external account, and an admin can link that external account to a billing customer later.

<!-- snippet: sample_external-account-link-events -->
<a id='snippet-sample_external-account-link-events'></a>
```cs
public interface IExternalAccountEvent
{
    string ExternalAccountId { get; }
}

public record CustomerRegistered(Guid CustomerId, string DisplayName);

public record CustomerLinkedToExternalAccount(Guid CustomerId, string ExternalAccountId);

public record ShippingLabelCreated(string ExternalAccountId): IExternalAccountEvent;

public record TrackingItemSeen(string ExternalAccountId, string Mode): IExternalAccountEvent;
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs#L19-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_external-account-link-events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Lookup document projected per external account:

<!-- snippet: sample_external-account-link -->
<a id='snippet-sample_external-account-link'></a>
```cs
public class ExternalAccountLink
{
    public required string Id { get; set; } // ExternalAccountId
    public required Guid CustomerId { get; set; }
}

public class ExternalAccountLinkProjection: SingleStreamProjection<ExternalAccountLink, string>
{
    public void Apply(CustomerLinkedToExternalAccount e, ExternalAccountLink link)
    {
        link.Id = e.ExternalAccountId;
        link.CustomerId = e.CustomerId;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs#L36-L53' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_external-account-link' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Custom grouper that resolves `CustomerId` in bulk per event range:

<!-- snippet: sample_external-account-link-grouper -->
<a id='snippet-sample_external-account-link-grouper'></a>
```cs
public class ExternalAccountToCustomerGrouper: IAggregateGrouper<Guid>
{
    public async Task Group(IQuerySession session, IEnumerable<IEvent> events, IEventGrouping<Guid> grouping)
    {
        var usageEvents = events
            .Where(e => e.Data is IExternalAccountEvent)
            .ToList();

        if (usageEvents.Count == 0) return;

        var externalIds = usageEvents
            .Select(e => ((IExternalAccountEvent)e.Data).ExternalAccountId)
            .Distinct()
            .ToList();

        var links = await session.Query<ExternalAccountLink>()
            .Where(x => externalIds.Contains(x.Id))
            .Select(x => new { x.Id, x.CustomerId })
            .ToListAsync();

        var map = links.ToDictionary(x => x.Id, x => x.CustomerId!);

        foreach (var @event in usageEvents)
        {
            var externalId = ((IExternalAccountEvent)@event.Data).ExternalAccountId;

            if (map.TryGetValue(externalId, out var customerId))
                grouping.AddEvent(customerId, @event);
        }
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs#L55-L89' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_external-account-link-grouper' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The multi stream projection stays focused on applying events:

<!-- snippet: sample_external-account-link-multi-stream-projection -->
<a id='snippet-sample_external-account-link-multi-stream-projection'></a>
```cs
public class CustomerBillingMetrics
{
    public Guid Id { get; set; }
    public int ShippingLabels { get; set; }
    public int TrackingEvents { get; set; }
    public HashSet<string> ModesSeen { get; set; } = [];
}

public class CustomerBillingProjection: MultiStreamProjection<CustomerBillingMetrics, Guid>
{
    public CustomerBillingProjection()
    {
        // notice you can mix custom grouping and Identity<T>(...)
        Identity<CustomerRegistered>(e => e.CustomerId);
        CustomGrouping(new ExternalAccountToCustomerGrouper());
    }

    public CustomerBillingMetrics Create(CustomerRegistered e)
        => new() { Id = e.CustomerId };

    public void Apply(CustomerBillingMetrics view, ShippingLabelCreated _)
        => view.ShippingLabels++;

    public void Apply(CustomerBillingMetrics view, TrackingItemSeen e)
    {
        view.TrackingEvents++;
        view.ModesSeen.Add(e.Mode);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs#L91-L123' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_external-account-link-multi-stream-projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Registration:

<!-- snippet: sample_external-account-link-lookup-registration -->
<a id='snippet-sample_external-account-link-lookup-registration'></a>
```cs
opts.Projections.Add<ExternalAccountLinkProjection>(ProjectionLifecycle.Inline);
opts.Projections.Add<CustomerBillingProjection>(ProjectionLifecycle.Async);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs#L129-L134' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_external-account-link-lookup-registration' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Pattern 2, keep the linked single stream ids on the projected document, then query by containment

::: danger
**Not recommended.** This pattern is racy under the async projection lifecycle.

If a link event (for example `CustomerLinkedToExternalAccount`) and a usage event
(for example `ShippingLabelCreated`) land in the **same** `SaveChangesAsync` batch,
the custom grouper queries `CustomerBillingMetrics.LinkedExternalAccounts` before
the link event has been applied to the aggregate in that batch cycle. The containment
query returns nothing, the usage event is silently dropped, and no exception is raised.

This is the same failure mode the general warning in [Custom Grouper](#custom-grouper)
describes: *"If your grouping logic requires you to access the aggregate view itself,
ViewProjection will not function correctly."* Pattern 2 violates that rule by querying
the projection being built.

If you must keep this shape (because you genuinely need the bounded linked-id list
on the projected document), the grouper has to be coded defensively:

1. Scan the current batch's `IEnumerable<IEvent>` for in-flight link events and seed
   an in-memory lookup from those first.
2. Then query the projected document (or a dedicated lookup) to cover links committed
   by an earlier batch.
3. Keep a grouper-instance or tenant-scoped cache of resolved links to avoid repeating
   the DB lookup across every daemon cycle.

That is essentially [Pattern 4](#pattern-4-batch-aware-grouper-with-in-memory-lookup-plus-db-fallback)
— so prefer Pattern 4 outright.
:::

Use this pattern only when all three of the following hold:

- The number of linked ids per aggregate stays small.
- The link event is guaranteed to precede the first usage event by at least one
  async daemon batch cycle (the link is "committed before usage").
- You cannot use Pattern 1 or Pattern 4.

#### Example

<!-- snippet: sample_external-account-link-id-list-grouper -->
<a id='snippet-sample_external-account-link-id-list-grouper'></a>
```cs
public class CustomerBillingMetrics
{
    public Guid Id { get; set; }
    public List<string> LinkedExternalAccounts { get; set; } = new();

    public int ShippingLabels { get; set; }
}

public class CustomerBillingProjection: MultiStreamProjection<CustomerBillingMetrics, Guid>
{
    public CustomerBillingProjection()
    {
        Identity<CustomerRegistered>(e => e.CustomerId);
        Identity<CustomerLinkedToExternalAccount>(e => e.CustomerId);

        CustomGrouping(async (session, events, grouping) =>
        {
            var labelEvents = events
                .OfType<IEvent<ShippingLabelCreated>>()
                .ToList();

            if (labelEvents.Count == 0) return;

            var externalIds = labelEvents
                .Select(x => x.Data.ExternalAccountId)
                .Distinct()
                .ToList();

            var owners = await session.Query<CustomerBillingMetrics>()
                .Where(x => x.LinkedExternalAccounts.Any(id => externalIds.Contains(id)))
                .Select(x => new { x.Id, x.LinkedExternalAccounts })
                .ToListAsync();

            var map = owners
                .SelectMany(o => o.LinkedExternalAccounts.Select(id => new { ExternalId = id, CustomerId = o.Id }))
                .ToDictionary(x => x.ExternalId, x => x.CustomerId);

            foreach (var e in labelEvents)
            {
                if (map.TryGetValue(e.Data.ExternalAccountId, out var customerId))
                    grouping.AddEvent(customerId, e);
            }
        });
    }

    public CustomerBillingMetrics Create(CustomerRegistered e)
        => new() { Id = e.CustomerId };

    public void Apply(CustomerBillingMetrics view, CustomerLinkedToExternalAccount e)
    {
        if (!view.LinkedExternalAccounts.Contains(e.ExternalAccountId))
            view.LinkedExternalAccounts.Add(e.ExternalAccountId);
    }

    public void Apply(CustomerBillingMetrics view, ShippingLabelCreated _)
        => view.ShippingLabels++;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs#L141-L200' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_external-account-link-id-list-grouper' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Pattern 4, batch-aware grouper with in-memory lookup plus DB fallback

Use this as the general-purpose fix for the same-batch race that breaks Pattern 2
and is only accidentally avoided by Pattern 1. It is the recommended shape whenever
link events and usage events can appear in a single `SaveChangesAsync` batch.

The idea is:

1. The grouper scans the current batch's `IEnumerable<IEvent>` for in-flight link
   events first, seeding an in-memory map from external id to aggregate id.
2. For any usage events whose external id is not in the map, the grouper queries
   a dedicated lookup document (the same one Pattern 1 uses) to pick up links
   committed by an earlier batch.
3. A grouper-instance cache (a `ConcurrentDictionary`, or equivalent) avoids
   repeating the DB lookup for external ids that have already been resolved.

Step 1 is what makes the pattern safe under same-batch ordering: by the time the
DB is consulted, any links sharing the batch have already been recorded in the
in-memory map.

#### Example

Events and the inline lookup projection are identical to Pattern 1 (`CustomerRegistered`,
`CustomerLinkedToExternalAccount`, `ShippingLabelCreated`, plus `ExternalAccountLink` /
`ExternalAccountLinkProjection`). Only the grouper and its registration differ:

<!-- snippet: sample_batch-aware-grouper -->
<a id='snippet-sample_batch-aware-grouper'></a>
```cs
public class CustomerBillingMetrics
{
    public Guid Id { get; set; }
    public int ShippingLabels { get; set; }
}

public class ExternalAccountLink
{
    public required string Id { get; set; }
    public required Guid CustomerId { get; set; }
}

public class ExternalAccountLinkProjection: SingleStreamProjection<ExternalAccountLink, string>
{
    public void Apply(CustomerLinkedToExternalAccount e, ExternalAccountLink link)
    {
        link.Id = e.ExternalAccountId;
        link.CustomerId = e.CustomerId;
    }
}

/// <summary>
/// Batch-aware grouper: consults in-batch link events first, then falls back to
/// a DB lookup for any external ids still unresolved. Maintains a small
/// grouper-instance cache to avoid repeated DB round-trips across daemon cycles.
/// </summary>
public class BatchAwareExternalAccountGrouper: IAggregateGrouper<Guid>
{
    private readonly ConcurrentDictionary<string, Guid> _cache = new();

    public async Task Group(IQuerySession session, IEnumerable<IEvent> events, IEventGrouping<Guid> grouping)
    {
        var materialized = events as IReadOnlyCollection<IEvent> ?? events.ToList();

        var labelEvents = materialized.OfType<IEvent<ShippingLabelCreated>>().ToList();
        if (labelEvents.Count == 0) return;

        // 1) Pick up any link events that share THIS batch.
        foreach (var linkEvent in materialized.OfType<IEvent<CustomerLinkedToExternalAccount>>())
        {
            _cache[linkEvent.Data.ExternalAccountId] = linkEvent.Data.CustomerId;
        }

        // 2) For any external ids still unresolved, query the lookup table.
        var unresolved = labelEvents
            .Select(x => x.Data.ExternalAccountId)
            .Distinct()
            .Where(id => !_cache.ContainsKey(id))
            .ToList();

        if (unresolved.Count > 0)
        {
            var links = await session.Query<ExternalAccountLink>()
                .Where(x => unresolved.Contains(x.Id))
                .Select(x => new { x.Id, x.CustomerId })
                .ToListAsync();

            foreach (var link in links)
            {
                _cache[link.Id] = link.CustomerId;
            }
        }

        // 3) Route each usage event to the matching customer id.
        foreach (var e in labelEvents)
        {
            if (_cache.TryGetValue(e.Data.ExternalAccountId, out var customerId))
            {
                grouping.AddEvent(customerId, e);
            }
        }
    }
}

public class CustomerBillingProjection: MultiStreamProjection<CustomerBillingMetrics, Guid>
{
    public CustomerBillingProjection()
    {
        Identity<CustomerRegistered>(e => e.CustomerId);
        CustomGrouping(new BatchAwareExternalAccountGrouper());
    }

    public CustomerBillingMetrics Create(CustomerRegistered e) => new() { Id = e.CustomerId };

    public void Apply(CustomerBillingMetrics view, ShippingLabelCreated _) => view.ShippingLabels++;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs#L220-L309' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_batch-aware-grouper' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Register the lookup projection inline and the multi-stream projection async, exactly
as in Pattern 1:

```cs
opts.Projections.Add<ExternalAccountLinkProjection>(ProjectionLifecycle.Inline);
opts.Projections.Add<CustomerBillingProjection>(ProjectionLifecycle.Async);
```

::: tip
The grouper-instance cache is safe because `IAggregateGrouper<TId>` is kept alive
for the lifetime of the projection registration. It will, however, grow without
bound in long-running processes if every external id is unique. Either add an LRU
eviction policy, or reset the cache periodically, if that matters for your workload.
:::

### Pattern 3, emit a derived event that contains the group key, using live aggregation plus the aggregate handler workflow

Use this when you only know enough information near the end of a process, and earlier events should not affect the multi stream read model yet.

The idea is:

1. Let fine grained events flow into their natural single stream
2. On a terminal command, load the current aggregate state, compute what you need
3. Return one derived event that contains the aggregate id plus the computed metrics
4. The multi stream projection becomes a simple `Identity` projection on that derived event

#### Example

Fine grained events:

<!-- snippet: sample_shipment-events -->
<a id='snippet-sample_shipment-events'></a>
```cs
public record ShipmentStarted(string ExternalAccountId, Guid CustomerId);

public record ItemScanned(string ItemId);

public record ShipmentCompleted;

public record ShipmentBilled(Guid CustomerId, Guid ShipmentId, int UniqueItems);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs#L314-L328' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_shipment-events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Live aggregate state:

<!-- snippet: sample_shipment -->
<a id='snippet-sample_shipment'></a>
```cs
public class Shipment
{
    public required string ExternalAccountId { get; set; }
    public required Guid CustomerId { get; set; }
    public HashSet<string> Items { get; set; } = [];

    public Shipment Create(ShipmentStarted e) => new()
    {
        ExternalAccountId = e.ExternalAccountId, CustomerId = e.CustomerId
    };

    public void Apply(ItemScanned e) => Items.Add(e.ItemId);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs#L330-L346' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_shipment' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Derived event that is projection friendly (includes `CustomerId` again):

<!-- snippet: sample_shipment-events-billed -->
<a id='snippet-sample_shipment-events-billed'></a>
```cs
public record ShipmentBilled(Guid CustomerId, Guid ShipmentId, int UniqueItems);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs#L322-L326' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_shipment-events-billed' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Command endpoint using the aggregate handler workflow, Wolverine loads the aggregate for you, you return the event, Wolverine appends it to the same stream:

```cs
public record CompleteShipment(int Version);

public static class CompleteShipmentEndpoint
{
    [WolverinePost("/api/shipments/{shipmentId:guid}/complete")]
    public static async Task<ShipmentBilled> Post(CompleteShipment command, [WriteAggregate] Shipment shipment)
    {
        return new ShipmentBilled(shipment.CustomerId, shipmentId, shipment.Items.Count);
    }
}
```

Now the multi stream projection is straightforward:

<!-- snippet: sample_shipment-events-multi-stream-projection -->
<a id='snippet-sample_shipment-events-multi-stream-projection'></a>
```cs
public class CustomerBillingMetrics
{
    public required Guid Id { get; set; } // CustomerId
    public required int Shipments { get; set; }
    public required int Items { get; set; }
}

public class CustomerBillingProjection: MultiStreamProjection<CustomerBillingMetrics, Guid>
{
    public CustomerBillingProjection()
    {
        Identity<ShipmentBilled>(e => e.CustomerId);
    }

    public CustomerBillingMetrics Create(ShipmentBilled e)
        => new() { Id = e.CustomerId, Shipments = 1, Items = e.UniqueItems };

    public void Apply(CustomerBillingMetrics view, ShipmentBilled e)
    {
        view.Shipments++;
        view.Items += e.UniqueItems;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs#L348-L374' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_shipment-events-multi-stream-projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
This style matches the [Wolverine aggregate handler workflow](https://wolverinefx.net/tutorials/cqrs-with-marten.html#appending-events-to-an-existing-stream) section about appending events by returning them from your endpoint or handler.
:::

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/rolling_up_by_tenant.cs#L92-L114' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rollup_projection_by_tenant_id' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do note that you'll probably also need this flag in your configuration:

```cs
// opts is a StoreOptions object
opts.Events.EnableGlobalProjectionsForConjoinedTenancy = true;
```

## Event "Fan Out" Rules

The `ViewProjection` also provides the ability to "fan out" child events from a parent event into the segment of events being used to
create an aggregated view. As an example, a `Travel` event we use in Marten testing contains a list of `Movement` objects:

<!-- snippet: sample_travel_movements -->
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

        Name = "Day";

        // Opt into 2nd level caching of up to 100
        // most recently encountered aggregates as a
        // performance optimization
        Options.CacheLimitPerTenant = 1000;

        // With large event stores of relatively small
        // event objects, moving this number up from the
        // default can greatly improve throughput and especially
        // improve projection rebuild times
        Options.BatchSize = 5000;
    }

    public void Apply(Day day, TripStarted e)
    {
        day.Started++;
    }

    public void Apply(Day day, TripEnded e)
    {
        day.Ended++;
    }

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

    public void Apply(Day day, Stop e)
    {
        day.Stops++;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/Aggregations/multi_stream_projections.cs#L250-L320' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_showing_fanout_rules' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/custom_grouper_with_events_transformation.cs#L43-L66' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-custom-grouper-with-transformation-projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Fan Out Using Custom Grouper

The custom grouper, `MonthlyAllocationGrouper`, is responsible for the logic of how events are grouped and fan-out.

<!-- snippet: sample_view-custom-grouper-with-transformation-grouper -->
<a id='snippet-sample_view-custom-grouper-with-transformation-grouper'></a>
```cs
public class MonthlyAllocationGrouper: IAggregateGrouper<string>
{
    public Task Group(IQuerySession session, IEnumerable<IEvent> events, IEventGrouping<string> grouping)
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/custom_grouper_with_events_transformation.cs#L68-L121' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-custom-grouper-with-transformation-grouper' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/CustomGroupers/custom_grouper_with_events_transformation.cs#L95-L105' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_view-custom-grouper-with-transformation-grouper-with-data' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Read also more in the [Event transformations, a tool to keep our processes loosely coupled](https://event-driven.io/en/event_transformations_and_loosely_coupling/?utm_source=marten_docs).

## Time-Based Segmentation: Monthly Activity per Account

A common real-world pattern is segmenting a single stream's events by time period — monthly
reports, daily summaries, billing periods, etc. Multi-stream projections handle this naturally
by routing events to documents with a **composite identity key** that combines the stream ID
with a time bucket.

This example builds a `MonthlyAccountActivity` read model that summarizes deposits, withdrawals,
and fees per account per calendar month. Each document's ID is `"{accountId}:{yyyy-MM}"`:

### Events

<!-- snippet: sample_monthly_account_activity_events -->
<a id='snippet-sample_monthly_account_activity_events'></a>
```cs
public record AccountOpened(string AccountName);
public record DepositRecorded(decimal Amount);
public record WithdrawalRecorded(decimal Amount);
public record FeeCharged(decimal Amount, string Reason);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/monthly_account_activity_projection.cs#L16-L23' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_monthly_account_activity_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Read Model

<!-- snippet: sample_monthly_account_activity_document -->
<a id='snippet-sample_monthly_account_activity_document'></a>
```cs
/// <summary>
/// Read model that summarizes account activity for a single calendar month.
/// The Id is a composite key: "{streamId}:{yyyy-MM}"
/// </summary>
public class MonthlyAccountActivity
{
    public string Id { get; set; } = "";
    public Guid AccountId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int TransactionCount { get; set; }
    public decimal TotalDeposits { get; set; }
    public decimal TotalWithdrawals { get; set; }
    public decimal TotalFees { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/monthly_account_activity_projection.cs#L25-L43' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_monthly_account_activity_document' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Projection

The key technique is `Identity<IEvent<T>>()` which gives you access to both the stream ID
(`e.StreamId`) and event metadata (`e.Timestamp`) to build the composite key:

<!-- snippet: sample_monthly_account_activity_projection -->
<a id='snippet-sample_monthly_account_activity_projection'></a>
```cs
public class MonthlyAccountActivityProjection : MultiStreamProjection<MonthlyAccountActivity, string>
{
    public MonthlyAccountActivityProjection()
    {
        // Route each event to a document keyed by "{accountId}:{yyyy-MM}"
        // using the stream ID (account) + event timestamp (month)
        Identity<IEvent<DepositRecorded>>(e =>
            $"{e.StreamId}:{e.Timestamp:yyyy-MM}");

        Identity<IEvent<WithdrawalRecorded>>(e =>
            $"{e.StreamId}:{e.Timestamp:yyyy-MM}");

        Identity<IEvent<FeeCharged>>(e =>
            $"{e.StreamId}:{e.Timestamp:yyyy-MM}");
    }

    public MonthlyAccountActivity Create(IEvent<DepositRecorded> e)
    {
        var (accountId, year, month) = ParseKey(e);
        return new MonthlyAccountActivity
        {
            AccountId = accountId, Year = year, Month = month,
            TransactionCount = 1, TotalDeposits = e.Data.Amount
        };
    }

    public void Apply(IEvent<DepositRecorded> e, MonthlyAccountActivity activity)
    {
        activity.TransactionCount++;
        activity.TotalDeposits += e.Data.Amount;
    }

    public MonthlyAccountActivity Create(IEvent<WithdrawalRecorded> e)
    {
        var (accountId, year, month) = ParseKey(e);
        return new MonthlyAccountActivity
        {
            AccountId = accountId, Year = year, Month = month,
            TransactionCount = 1, TotalWithdrawals = e.Data.Amount
        };
    }

    public void Apply(IEvent<WithdrawalRecorded> e, MonthlyAccountActivity activity)
    {
        activity.TransactionCount++;
        activity.TotalWithdrawals += e.Data.Amount;
    }

    public MonthlyAccountActivity Create(IEvent<FeeCharged> e)
    {
        var (accountId, year, month) = ParseKey(e);
        return new MonthlyAccountActivity
        {
            AccountId = accountId, Year = year, Month = month,
            TransactionCount = 1, TotalFees = e.Data.Amount
        };
    }

    public void Apply(IEvent<FeeCharged> e, MonthlyAccountActivity activity)
    {
        activity.TransactionCount++;
        activity.TotalFees += e.Data.Amount;
    }

    private static (Guid AccountId, int Year, int Month) ParseKey(IEvent e)
    {
        return (e.StreamId, e.Timestamp.Year, e.Timestamp.Month);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/MultiStreamProjections/monthly_account_activity_projection.cs#L45-L117' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_monthly_account_activity_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Registration

```cs
builder.Services.AddMarten(opts =>
{
    // Register as Async for production use with the daemon
    opts.Projections.Add<MonthlyAccountActivityProjection>(ProjectionLifecycle.Async);
});
```

### How It Works

When events are appended to an account stream, the projection routes each event to a
document based on `{streamId}:{yyyy-MM}`. If three deposits happen in January and two
in February, you get two separate `MonthlyAccountActivity` documents:

- `"{accountId}:2026-01"` — 3 transactions, January totals
- `"{accountId}:2026-02"` — 2 transactions, February totals

This pattern works because `Identity<IEvent<T>>()` gives you access to:

- **`e.StreamId`** — the account's stream identity (Guid)
- **`e.Timestamp`** — the event's timestamp for time bucketing
- **`e.Data`** — the event payload for any additional routing logic

You can adapt this pattern for any time granularity (daily, weekly, quarterly) by
changing the format string in the identity expression.
