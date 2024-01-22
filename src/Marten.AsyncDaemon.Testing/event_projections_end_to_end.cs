using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing;

public class event_projections_end_to_end : DaemonContext
{
    public event_projections_end_to_end(ITestOutputHelper output) : base(output)
    {
        _output = output;
    }

    [Fact]
    public void uses_event_type_filter()
    {
        var projection = new DistanceProjection();
        projection.AssembleAndAssertValidity();
        var filter = projection.As<IProjectionSource>()
            .AsyncProjectionShards(TheStore)
            .First()
            .EventFilters
            .OfType<Marten.Events.Daemon.EventTypeFilter>()
            .Single();

        filter.EventTypes.Single().ShouldBe(typeof(Travel));
    }

    #region sample_using_WaitForNonStaleProjectionDataAsync

    [Fact]
    public async Task run_simultaneously()
    {
        StoreOptions(x => x.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Async));

        NumberOfStreams = 10;

        var agent = await StartDaemon();

        // This method publishes a random number of events
        await PublishSingleThreaded();

        // Wait for all projections to reach the highest event sequence point
        // as of the time this method is called
        await TheStore.WaitForNonStaleProjectionDataAsync(15.Seconds());

        await CheckExpectedResults();
    }

    #endregion

    [Fact]
    public async Task run_simultaneously_multitenancy()
    {
        StoreOptions(x =>
        {
            x.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Async);
            x.Events.TenancyStyle = TenancyStyle.Conjoined;
            x.Schema.For<Distance>().MultiTenanted();
        });

        UseMixOfTenants(10);

        var agent = await StartDaemon();

        await PublishSingleThreaded();

        await TheStore.WaitForNonStaleProjectionDataAsync(15.Seconds());

        await CheckExpectedResultsForTenants("a", "b");
    }

    [Fact]
    public async Task rebuild()
    {
        NumberOfStreams = 10;

        #region sample_rebuild-single-projection
        StoreOptions(x => x.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Async));

        var agent = await StartDaemon();

        // setup test data
        await PublishSingleThreaded();

        // rebuild projection `Distance`
        await agent.RebuildProjection("Distance", CancellationToken.None);
        #endregion

    }

    private Task CheckExpectedResults()
    {
        return CheckExpectedResults(TheSession);
    }

    private async Task CheckExpectedResultsForTenants(params string[] tenants)
    {
        foreach (var tenantId in tenants)
        {
            await using (var session = TheStore.LightweightSession(tenantId))
            {
                await CheckExpectedResults(session);
            }
        }
    }



    private async Task CheckExpectedResults(IDocumentSession session)
    {
        var distances = await session.Query<Distance>().ToListAsync();

        var events = (await session.Events.QueryAllRawEvents().ToListAsync());
        var travels = events.OfType<Event<Travel>>().ToDictionary(x => x.Id);

        foreach (var distance in distances)
        {
            if (travels.TryGetValue(distance.Id, out var travel))
            {
                distance.Day.ShouldBe(travel.Data.Day);
                distance.Total.ShouldBe(travel.Data.TotalDistance());
            }
            else
            {
                travel.ShouldNotBeNull();
            }

            Logger.LogDebug("Compared distance " + distance);
        }
    }
}

public class Distance
{
    public Guid Id { get; set; }
    public double Total { get; set; }
    public int Day { get; set; }

    public override string ToString()
    {
        return $"{nameof(Id)}: {Id}, {nameof(Total)}: {Total}, {nameof(Day)}: {Day}";
    }
}

#region sample_using_create_in_event_projection

public class DistanceProjection: EventProjection
{
    public DistanceProjection()
    {
        ProjectionName = "Distance";
    }

    // Create a new Distance document based on a Travel event
    public Distance Create(Travel travel, IEvent e)
    {
        return new Distance {Id = e.Id, Day = travel.Day, Total = travel.TotalDistance()};
    }
}

#endregion

public class DistanceProjection2: SyncProjectionBase
{
    public override void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
    {
        foreach (var @event in streams.SelectMany(x => x.Events))
        {
            switch (@event.Data)
            {
                case IEvent<Travel> e:
                    var travel = e.Data;
                    var distance = new Distance
                    {
                        Id = e.Id,
                        Day = travel.Day,
                        Total = travel.TotalDistance()
                    };
                    operations.Store(distance);
                    break;
            }
        }
    }
}
