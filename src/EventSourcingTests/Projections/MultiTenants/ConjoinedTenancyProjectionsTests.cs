using System;
using System.Threading.Tasks;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections.MultiTenants;

public class ConjoinedTenancyProjectionsTests: IntegrationContext
{
    public ConjoinedTenancyProjectionsTests(DefaultStoreFixture fixture): base(fixture)
    {
    }

    [Fact]
    public async Task ForEventsAppendedToTenantedSession_AndConjoinedTenancyProjection_ShouldBeUpdated()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.EnableGlobalProjectionsForConjoinedTenancy = true;

            opts.Schema.For<ResourcesGlobalSummary>().SingleTenanted();

            opts.Projections.Add<ResourceProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<ResourcesGlobalSummaryProjection>(ProjectionLifecycle.Inline);
        });

        var organisationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid().ToString();
        var resourceName = "Test";

        var resourceId = await StartStreamForTenant(tenantId, new ResourceCreatedEvent(resourceName, organisationId));
        await AssertProjectionUpdatedForTenant(tenantId, ResourceState.Enabled);

        await AppendEventForTenant(tenantId, new ResourceEnabledEvent(organisationId));
        await AssertProjectionUpdatedForTenant(tenantId, ResourceState.Enabled);

        await AppendEventForTenant(tenantId, new ResourceDisabledEvent(organisationId));
        await AssertProjectionUpdatedForTenant(tenantId, ResourceState.Disabled);

        await AppendEventForTenant(tenantId, new ResourceEnabledEvent(organisationId));
        await AssertProjectionUpdatedForTenant(tenantId, ResourceState.Enabled);

        var otherTenantId = Guid.NewGuid().ToString();
        await StartStreamForTenant(otherTenantId, new ResourceCreatedEvent("doesn't matter", organisationId));

        await AssertGlobalProjectionUpdatedForTenant();

        async Task<Guid> StartStreamForTenant(string tenant, ResourceCreatedEvent @event)
        {
            var startStream = theSession.ForTenant(tenant)
                .Events.StartStream(@event);
            await theSession.SaveChangesAsync();

            return startStream.Id;
        }

        Task AppendEventForTenant(string tenant, object @event)
        {
            theSession.ForTenant(tenant)
                .Events.Append(resourceId, @event);

            return theSession.SaveChangesAsync();
        }

        async Task AssertProjectionUpdatedForTenant(string tenant, ResourceState status)
        {
            var resource = await theSession.ForTenant(tenant)
                .Query<Resource>().SingleOrDefaultAsync(r => r.Id == resourceId);

            resource.ShouldNotBeNull();
            resource.Id.ShouldBe(resourceId);
            resource.Name.ShouldBe(resourceName);
            resource.State.ShouldBe(status);
        }

        async Task AssertGlobalProjectionUpdatedForTenant()
        {
            var resource = await theSession.ForTenant(Tenancy.DefaultTenantId)
                .Query<ResourcesGlobalSummary>().SingleOrDefaultAsync(r => r.Id == organisationId);

            resource.ShouldNotBeNull();
            resource.Id.ShouldBe(organisationId);
            resource.TotalResourcesCount.ShouldBe(2);
        }
    }
}

public record Event;

public record ResourceCreatedEvent(string Name, Guid OrganisationId): Event;

public record ResourceRemovedEvent(Guid OrganisationId): Event;

public record ResourceEnabledEvent(Guid OrganisationId): Event;

public record ResourceDisabledEvent(Guid OrganisationId): Event;

public record Resource
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public ResourceState State { get; set; }
}

public enum ResourceState
{
    Disabled,
    Enabled
}

public class ResourceProjection: SingleStreamProjection<Resource>
{
    public ResourceProjection() =>
        DeleteEvent<ResourceRemovedEvent>();

    public void Apply(ResourceDisabledEvent e, Resource resource) =>
        resource.State = ResourceState.Disabled;

    public void Apply(ResourceEnabledEvent e, Resource resource) =>
        resource.State = ResourceState.Enabled;

    public Resource Create(ResourceCreatedEvent create) =>
        new() { Name = create.Name, State = ResourceState.Enabled };
}

public record ResourcesGlobalSummary
{
    public Guid Id { get; set; }
    public int TotalResourcesCount { get; set; }
}

public class ResourcesGlobalSummaryProjection: MultiStreamProjection<ResourcesGlobalSummary, Guid>
{
    public ResourcesGlobalSummaryProjection()
    {
        Identity<ResourceCreatedEvent>(e => e.OrganisationId);
        Identity<ResourceRemovedEvent>(e => e.OrganisationId);
    }

    public void Apply(ResourceCreatedEvent e, ResourcesGlobalSummary resourceGlobal) =>
        resourceGlobal.TotalResourcesCount++;

    public void Apply(ResourceRemovedEvent e, ResourcesGlobalSummary resourceGlobal) =>
        resourceGlobal.TotalResourcesCount--;
}
