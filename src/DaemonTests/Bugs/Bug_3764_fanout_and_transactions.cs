using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.Aggregations;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Bugs;

public class Bug_3764_fanout_and_transactions : BugIntegrationContext
{
    [Fact]
    public async Task correctly_apply_fanout_while_Inline()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<DayProjection>(ProjectionLifecycle.Inline);
        });

        var session = theSession;

        var trip = new Trip{Id = Guid.NewGuid()};

        var random = Random.Shared;
        var startDay = random.Next(1, 100);

        var tripStarted = new TripStarted { Day =startDay };

        session.Events.Append(trip.Id, tripStarted);

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        var duration = random.Next(1, 20);

        for (var index = 0; index < duration; index++)
        {
            var day = startDay + index;

            var travel = Travel.Random(day);

            session.Events.Append(trip.Id, travel);
        }

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tripEnded = new TripEnded{Day = startDay + duration};

        session.Events.Append(trip.Id, tripEnded);

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Load as Inline
        var days = await session.Query<Day>().ToListAsync(TestContext.Current.CancellationToken);


        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<Day>(CancellationToken.None);

        var expected = await session.Query<Day>().ToListAsync(TestContext.Current.CancellationToken);

        days.OrderBy(x => x.Id).ShouldBe(expected.OrderBy(x => x.Id));

        Debug.WriteLine(days);
    }
}
