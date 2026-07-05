using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using FS = FSharpProjections;

namespace EventSourcingTests.FSharp;

// #4796 — F# projections must override the explicit Evolve/EvolveAsync/ApplyAsync
// methods (F# has no partial classes and no Roslyn source generators, so the
// conventional Apply/Create convention methods are unavailable). These tests
// exercise each F# authoring path so a regression that breaks F# projection
// authoring is caught by the build + test suite.
public class fsharp_projections : OneOffConfigurationsContext
{
    [Fact]
    public async Task self_aggregating_single_stream_sync_evolve()
    {
        StoreOptions(opts => opts.Projections.Add(new FS.AccountProjection(), ProjectionLifecycle.Inline));

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream(streamId,
            new FS.AccountCredited(100m),
            new FS.AccountDebited(30m),
            new FS.AccountCredited(5m));
        await theSession.SaveChangesAsync();

        var account = await theSession.LoadAsync<FS.Account>(streamId);
        account.ShouldNotBeNull();
        account.Balance.ShouldBe(75m);
    }

    [Fact]
    public async Task single_stream_async_evolve()
    {
        StoreOptions(opts => opts.Projections.Add(new FS.OrderSummaryProjection(), ProjectionLifecycle.Inline));

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream(streamId,
            new FS.OrderPlaced(2),
            new FS.OrderPlaced(3),
            new FS.OrderShipped("DHL"));
        await theSession.SaveChangesAsync();

        var summary = await theSession.LoadAsync<FS.OrderSummary>(streamId);
        summary.ShouldNotBeNull();
        summary.ItemCount.ShouldBe(5);
        summary.Shipped.ShouldBeTrue();
    }

    [Fact]
    public async Task multi_stream_sync_evolve()
    {
        StoreOptions(opts => opts.Projections.Add(new FS.LocationOccupancyProjection(), ProjectionLifecycle.Inline));

        // Two separate streams, same location -> aggregated into one document keyed by location
        theSession.Events.StartStream(Guid.NewGuid(), new FS.GuestArrived("HQ"), new FS.GuestArrived("HQ"));
        theSession.Events.StartStream(Guid.NewGuid(), new FS.GuestArrived("HQ"), new FS.GuestDeparted("HQ"));
        await theSession.SaveChangesAsync();

        var occupancy = await theSession.LoadAsync<FS.LocationOccupancy>("HQ");
        occupancy.ShouldNotBeNull();
        occupancy.Guests.ShouldBe(2);
    }

    [Fact]
    public async Task multi_stream_async_evolve()
    {
        StoreOptions(opts => opts.Projections.Add(new FS.RegionRevenueProjection(), ProjectionLifecycle.Inline));

        theSession.Events.StartStream(Guid.NewGuid(), new FS.SaleRecorded("West", 100m));
        theSession.Events.StartStream(Guid.NewGuid(), new FS.SaleRecorded("West", 250m));
        await theSession.SaveChangesAsync();

        var revenue = await theSession.LoadAsync<FS.RegionRevenue>("West");
        revenue.ShouldNotBeNull();
        revenue.Total.ShouldBe(350m);
    }

    [Fact]
    public async Task event_projection_apply_async()
    {
        StoreOptions(opts => opts.Projections.Add(new FS.UserEmailProjection(), ProjectionLifecycle.Inline));

        var userId = Guid.NewGuid();
        theSession.Events.StartStream(Guid.NewGuid(),
            new FS.EmailChanged(userId, "first@example.com"),
            new FS.EmailChanged(userId, "second@example.com"));
        await theSession.SaveChangesAsync();

        var email = await theSession.LoadAsync<FS.UserEmail>(userId);
        email.ShouldNotBeNull();
        email.Email.ShouldBe("second@example.com");
    }
}
