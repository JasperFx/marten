using System;
using System.Linq;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Resiliency;

// marten#4546 / jasperfx#356: MartenDatabase implements the new IEventDatabase
// dead-letter count read. Reuses the poison-event projections from
// when_skipping_events_in_daemon (NamedDocuments / CollateNames) whose dead-letter
// outcome is deterministic: NamedDocuments:All -> 6 (seqs 4,5,6,7,11,14),
// CollateNames:All -> 4 (seqs 4,5,6,7).
public class dead_letter_count_read_tests : DaemonContext
{
    private readonly string[] theNames =
    {
        "Jane", "Jill", "Jack", "JohnBad", "JakeBad", "JillBad", "JohnBad",
        "Derrick", "Daniel", "Donald", "DonBad", "Bob", "Beck", "BadName", "Jeremy"
    };

    public dead_letter_count_read_tests(ITestOutputHelper output) : base(output)
    {
        StoreOptions(opts =>
        {
            opts.Events.DatabaseSchemaName = "daemon";
            opts.Projections.Add<ErrorRejectingEventProjection>(ProjectionLifecycle.Async);
            opts.Projections.Add<CollateNames>(ProjectionLifecycle.Async);

            opts.Projections.RebuildErrors.SkipApplyErrors = true;
            opts.Projections.Errors.SkipApplyErrors = true;
        });
    }

    [Fact]
    public async Task fetch_and_count_dead_letter_events()
    {
        var daemon = await StartDaemon();

        var waiter1 = daemon.Tracker.WaitForShardState("CollateNames:All", theNames.Length);
        var waiter2 = daemon.Tracker.WaitForShardState("NamedDocuments:All", theNames.Length, 5.Minutes());

        var events = theNames.Select(name => new NameEvent { Name = name }).OfType<object>().ToArray();
        theSession.Events.StartStream(Guid.NewGuid(), events);
        await theSession.SaveChangesAsync();

        await daemon.Tracker.WaitForHighWaterMark(theNames.Length);
        await waiter1;
        await waiter2;

        // Drain the dead letter events queued up
        await daemon.StopAllAsync();

        var database = (IEventDatabase)theStore.Tenancy.Default.Database;

        // Bulk: one row per (ProjectionName, ShardKey)
        var counts = await database.FetchDeadLetterCountsAsync();

        counts.Single(x => x.ProjectionName == "NamedDocuments" && x.ShardKey == "All").Count.ShouldBe(6);
        counts.Single(x => x.ProjectionName == "CollateNames" && x.ShardKey == "All").Count.ShouldBe(4);

        // Per-shard
        (await database.CountDeadLetterEventsAsync(new ShardName("NamedDocuments"))).ShouldBe(6);
        (await database.CountDeadLetterEventsAsync(new ShardName("CollateNames"))).ShouldBe(4);

        // A shard with no dead letters returns 0
        (await database.CountDeadLetterEventsAsync(new ShardName("DoesNotExist"))).ShouldBe(0);
    }
}
