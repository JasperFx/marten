using System.Linq;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests;

public class event_projections_end_to_end_ihost : DaemonContext
{
    public event_projections_end_to_end_ihost(ITestOutputHelper output) : base(output)
    {
        _output = output;
    }

    #region sample_accessing_daemon_from_ihost

    [Fact]
    public async Task run_simultaneously()
    {
        var host = await StartDaemonInHotColdMode();

        StoreOptions(x => x.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Async));

        NumberOfStreams = 10;

        var agent = await StartDaemon();

        // This method publishes a random number of events
        await PublishSingleThreaded();

        // Wait for all projections to reach the highest event sequence point
        // as of the time this method is called
        await host.WaitForNonStaleProjectionDataAsync(15.Seconds());

        await CheckExpectedResults();
    }

    #endregion

    private Task CheckExpectedResults()
    {
        return CheckExpectedResults(theSession);
    }

    private async Task CheckExpectedResultsForTenants(params string[] tenants)
    {
        foreach (var tenantId in tenants)
        {
            await using (var session = theStore.LightweightSession(tenantId))
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

        distances.Count.ShouldBe(travels.Count);
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
