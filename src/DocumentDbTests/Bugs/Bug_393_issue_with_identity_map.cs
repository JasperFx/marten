using System;
using System.Threading.Tasks;
using Marten.Schema.Identity;
using Marten.Testing.Harness;
using Xunit;
using Shouldly;

namespace DocumentDbTests.Bugs;

public class Bug_393_issue_with_identity_map: IntegrationContext
{
    [Fact]
    public async Task load_non_existing_with_a_store_should_return_new_added_document()
    {
        var routeId = CombGuidIdGeneration.NewGuid();

        using var session = theStore.IdentitySession();
        var details = await session.LoadAsync<RouteDetails>(routeId);
        details.ShouldBeNull();

        var routeDetails = new RouteDetails { Id = routeId };
        session.Store(routeDetails);

        details = await session.LoadAsync<RouteDetails>(routeId); // this was always null

        details.ShouldBeSameAs(routeDetails);
    }

    public Bug_393_issue_with_identity_map(DefaultStoreFixture fixture): base(fixture)
    {
    }
}

public class Bug_393_issue_with_dirty_tracking_identity_map: IntegrationContext
{
    [Fact]
    public async Task load_non_existing_with_a_store_should_return_new_added_document()
    {
        var routeId = CombGuidIdGeneration.NewGuid();

        using var session = theStore.DirtyTrackedSession();
        var details = await session.LoadAsync<RouteDetails>(routeId);
        details.ShouldBeNull();

        var routeDetails = new RouteDetails { Id = routeId };
        session.Store(routeDetails);

        details = await session.LoadAsync<RouteDetails>(routeId);

        details.ShouldBeSameAs(routeDetails);
    }

    public Bug_393_issue_with_dirty_tracking_identity_map(DefaultStoreFixture fixture): base(fixture)
    {
    }
}

public class RouteDetails
{
    public Guid Id { get; set; }
    public DateTime PlannedDate { get; set; }
    public DateTime PlannedSince { get; set; }
    public DateTime DrivingDate { get; set; }

    public string Status { get; set; }
}
