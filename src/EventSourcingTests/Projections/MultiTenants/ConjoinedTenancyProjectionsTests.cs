using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
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
            var resource = await theSession.ForTenant(StorageConstants.DefaultTenantId)
                .Query<ResourcesGlobalSummary>().SingleOrDefaultAsync(r => r.Id == organisationId);

            resource.ShouldNotBeNull();
            resource.Id.ShouldBe(organisationId);
            resource.TotalResourcesCount.ShouldBe(2);
        }
    }

    [Fact]
    public async Task ForEventsAppendedToTenantedSession_CustomProjection()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;

            opts.Schema.For<CompanyLocation>().SoftDeleted();
            opts.Projections.Add(new CompanyLocationCustomProjection(), ProjectionLifecycle.Inline);
        });

        var tenantId = Guid.NewGuid().ToString();
        var companyLocationId = Guid.NewGuid();
        var companyLocationName = "New York";

        CompanyLocationCustomProjection.ExpectedTenant = tenantId;

        // theSession is for the default-tenant
        // we switch to another tenant, and append events there
        // the projected document should also be saved for that other tenant, NOT the default-tenant
        theSession.ForTenant(tenantId).Events.StartStream(companyLocationId, new CompanyLocationCreated(companyLocationName));

        await theSession.SaveChangesAsync();

        var defaultTenantCompanyLocations = await theSession.Query<CompanyLocation>().ToListAsync();
        defaultTenantCompanyLocations.ShouldBeEmpty();

        var otherTenantCompanyLocations = await theSession.ForTenant(tenantId).Query<CompanyLocation>().ToListAsync();
        var singleCompanyLocation = otherTenantCompanyLocations.SingleOrDefault();

        singleCompanyLocation.ShouldNotBeNull();
        singleCompanyLocation.Id.ShouldBe(companyLocationId);
        singleCompanyLocation.Name.ShouldBe(companyLocationName);
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

public class ResourceProjection: SingleStreamProjection<Resource, Guid>
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
        TenancyGrouping = TenancyGrouping.AcrossTenants;
        Identity<ResourceCreatedEvent>(e => e.OrganisationId);
        Identity<ResourceRemovedEvent>(e => e.OrganisationId);
    }

    public void Apply(ResourceCreatedEvent e, ResourcesGlobalSummary resourceGlobal) =>
        resourceGlobal.TotalResourcesCount++;

    public void Apply(ResourceRemovedEvent e, ResourcesGlobalSummary resourceGlobal) =>
        resourceGlobal.TotalResourcesCount--;
}

public record CompanyLocation
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}
public record CompanyLocationCreated(string Name);
public record CompanyLocationUpdated(string NewName);
public record CompanyLocationDeleted();

public class CompanyLocationCustomProjection : SingleStreamProjection<CompanyLocation, Guid>
{
    public static string ExpectedTenant;

    public CompanyLocationCustomProjection()
    {
        this.IncludeType<CompanyLocationCreated>();
        this.IncludeType<CompanyLocationUpdated>();
        this.IncludeType<CompanyLocationDeleted>();
    }

    public override ValueTask<(CompanyLocation, ActionType)> DetermineActionAsync(IQuerySession session, CompanyLocation snapshot, Guid identity,
        IIdentitySetter<CompanyLocation, Guid> identitySetter, IReadOnlyList<IEvent> events, CancellationToken cancellation)
    {
        var location = snapshot;

        // The session and the slice should be for the same tenant
        session.TenantId.ShouldBe(ExpectedTenant);

        foreach (var data in events.Select(x => x.Data))
        {
            switch (data)
            {
                case CompanyLocationCreated c:
                    location = new CompanyLocation
                    {
                        Id = identity,
                        Name = c.Name,
                    };

                    break;

                case CompanyLocationUpdated u:
                    location.Name = u.NewName;
                    break;

                case CompanyLocationDeleted d:
                    return new ValueTask<(CompanyLocation, ActionType)>((location, ActionType.Delete));
            }
        }

        return new ValueTask<(CompanyLocation, ActionType)>((location, ActionType.Store));
    }

}
