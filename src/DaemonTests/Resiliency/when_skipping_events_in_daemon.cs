using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bobcat;
using DaemonTests.TestingSupport;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Linq;
using Marten.Schema;
using Marten.Testing.Harness;
using Marten.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Xunit;

namespace DaemonTests.Resiliency;

[BobcatFeature("Skipping events that fail to apply")]
public class when_skipping_events_in_daemon : DaemonContext
{
    private readonly string[] theNames =
    {
        "Jane",
        "Jill",
        "Jack",
        "JohnBad",
        "JakeBad",
        "JillBad",
        "JohnBad",
        "Derrick",
        "Daniel",
        "Donald",
        "DonBad",
        "Bob",
        "Beck",
        "BadName",
        "Jeremy"
    };

    public when_skipping_events_in_daemon(ITestOutputHelper output) : base(output)
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



    [Given("the names are published to a running daemon, the bad ones failing to apply")]
    internal async Task<IProjectionDaemon> PublishTheEvents()
    {
        var daemon = await StartDaemon();

        var events = theNames.Select(name => new NameEvent {Name = name})
            .OfType<object>()
            .ToArray();

        theSession.Events.StartStream(Guid.NewGuid(), events);
        await theSession.SaveChangesAsync();

        await daemon.Tracker.WaitForHighWaterMark(theNames.Length);

        await WaitForShard(daemon, "CollateNames:All", theNames.Length, typeof(NamesByLetter));
        await WaitForShard(daemon, "NamedDocuments:All", theNames.Length, typeof(NamedDocument), 5.Minutes());

        return daemon;
    }

    [Fact]
    public async Task the_shards_should_still_be_running()
    {
        var daemon = await PublishTheEvents();

        // Then every shard is still running
        var shards = daemon.CurrentAgents();

        foreach (var shard in shards)
        {
            shard.Status.ShouldBe(AgentStatus.Running);
        }
    }

    [Fact]
    public async Task skip_bad_events_in_event_projection()
    {
        await PublishTheEvents();

        // Then the event projection holds only the good names
        var names = await theSession.Query<NamedDocument>()
            .OrderBy(x => x.Name)
            .Select(x => x.Name)
            .ToListAsync(TestContext.Current.CancellationToken);

        var expected = theNames
            .OrderBy(x => x)
            .Where(x => !x.Contains("bad", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        names.ShouldBe(expected);
    }

    [Fact]
    public async Task skip_bad_events_in_aggregate_projection()
    {
        await PublishTheEvents();

        // Then the aggregate holds only the good J names
        var jNames = await theSession.LoadAsync<NamesByLetter>("J", TestContext.Current.CancellationToken);

        jNames.Names.OrderBy(x => x).ShouldBe(["Jack", "Jane", "Jeremy", "Jill"]);
    }

    [Fact]
    public async Task see_the_dead_letter_events()
    {
        var daemon = await PublishTheEvents();

        // When the daemon stops, draining the dead letter events it queued up
        await daemon.StopAllAsync();

        #region sample_replacing_logger_per_session
        // We frequently use this special marten logger per
        // session to pipe Marten logging to the xUnit.Net output
        theSession.Logger = new TestOutputMartenLogger(_output);
        #endregion
        // Then each skipped event is a dead letter for the projection that skipped it
        var skipped = await theSession.Query<DeadLetterEvent>().ToListAsync(TestContext.Current.CancellationToken);

        // Each subject is a local first: Bobcat.Generators 1.0.0-alpha.1 writes a projected assertion's
        // subject into a string literal without escaping its newlines, so a chain split across lines
        // stops the build
        var namedDocumentsSkipped = skipped.Where(x => x.ProjectionName == "NamedDocuments" && x.ShardName == "All")
            .Select(x => x.EventSequence).OrderBy(x => x).ToList();
        namedDocumentsSkipped.ShouldBe([4L, 5, 6, 7, 11, 14]);

        var collateNamesSkipped = skipped.Where(x => x.ProjectionName == "CollateNames" && x.ShardName == "All")
            .Select(x => x.EventSequence).OrderBy(x => x).ToList();
        collateNamesSkipped.ShouldBe([4L, 5, 6, 7]);

    }
}

public class when_skipping_events_in_daemon_with_advanced_tracking : DaemonContext
{
    private readonly string[] theNames =
    {
        "Jane",
        "Jill",
        "Jack",
        "JohnBad",
        "JakeBad",
        "JillBad",
        "JohnBad",
        "Derrick",
        "Daniel",
        "Donald",
        "DonBad",
        "Bob",
        "Beck",
        "BadName",
        "Jeremy"
    };

    public when_skipping_events_in_daemon_with_advanced_tracking(ITestOutputHelper output) : base(output)
    {
        StoreOptions(opts =>
        {
            opts.Events.DatabaseSchemaName = "daemon";
            opts.Projections.Add<ErrorRejectingEventProjection>(ProjectionLifecycle.Async);
            opts.Projections.Add<CollateNames>(ProjectionLifecycle.Async);

            opts.Projections.RebuildErrors.SkipApplyErrors = true;
            opts.Projections.Errors.SkipApplyErrors = true;

            opts.Events.EnableAdvancedAsyncTracking = true;
        });
    }

    public static void enable_advanced_tracking_in_bootstrapping()
    {
        #region sample_enabling_advanced_tracking

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));

            opts.Events.EnableAdvancedAsyncTracking = true;
        });

        #endregion
    }

    private async Task<IProjectionDaemon> PublishTheEvents()
    {
        var daemon = await StartDaemon();

        var waiter1 = daemon.Tracker.WaitForShardState("CollateNames:All", theNames.Length);
        var waiter2 = daemon.Tracker.WaitForShardState("NamedDocuments:All", theNames.Length, 5.Minutes());


        var events = theNames.Select(name => new NameEvent {Name = name})
            .OfType<object>()
            .ToArray();

        theSession.Events.StartStream(Guid.NewGuid(), events);
        await theSession.SaveChangesAsync();

        await daemon.Tracker.WaitForHighWaterMark(theNames.Length);

        await waiter1;
        await waiter2;

        return daemon;
    }

    [Fact]
    public async Task the_shards_should_still_be_running()
    {
        var daemon = await PublishTheEvents();

        var shards = daemon.CurrentAgents();

        foreach (var shard in shards)
        {
            shard.Status.ShouldBe(AgentStatus.Running);
        }
    }

    [Fact]
    public async Task skip_bad_events_in_event_projection()
    {
        await PublishTheEvents();

        var names = await theSession.Query<NamedDocument>()
            .OrderBy(x => x.Name)
            .Select(x => x.Name)
            .ToListAsync(TestContext.Current.CancellationToken);

        var expected = theNames
            .OrderBy(x => x)
            .Where(x => !x.Contains("bad", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        names.ShouldHaveTheSameElementsAs(expected);
    }

    [Fact]
    public async Task skip_bad_events_in_aggregate_projection()
    {
        await PublishTheEvents();

        var jNames = await theSession.LoadAsync<NamesByLetter>("J", TestContext.Current.CancellationToken);

        jNames.Names.OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("Jack", "Jane", "Jeremy", "Jill");
    }

    [Fact]
    public async Task see_the_dead_letter_events()
    {
        var daemon = await PublishTheEvents();

        // Drain the dead letter events queued up
        await daemon.StopAllAsync();

        var skipped = await theSession.Query<DeadLetterEvent>().ToListAsync(TestContext.Current.CancellationToken);

        skipped.Where(x => x.ProjectionName == "CollateNames" && x.ShardName == "All")
            .Select(x => x.EventSequence).OrderBy(x => x)
            .ShouldHaveTheSameElementsAs(4, 5, 6, 7);

        skipped.Where(x => x.ProjectionName == "NamedDocuments" && x.ShardName == "All")
            .Select(x => x.EventSequence).OrderBy(x => x)
            .ShouldHaveTheSameElementsAs(4, 5, 6, 7, 11, 14);

    }
}

public partial class ErrorRejectingEventProjection: EventProjection
{
    public ErrorRejectingEventProjection()
    {
        Name = "NamedDocuments";
    }

    public NamedDocument Create(NameEvent e)
    {
        if (e.Name.Contains("bad", StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception("Bad name.");
        }

        return new NamedDocument {Name = e.Name};
    }
}

public partial class CollateNames: MultiStreamProjection<NamesByLetter, string>
{
    public CollateNames()
    {
        Name = "CollateNames";
        Identity<NameEvent>(e => e.Name.First().ToString());
    }

    public void Apply(NameEvent e, NamesByLetter names)
    {
        if (e.Name.Contains("bad", StringComparison.OrdinalIgnoreCase) && e.Name.StartsWith("J"))
        {
            throw new Exception("Bad name.");
        }

        names.Names.Add(e.Name);
    }
}

public class NamesByLetter
{
    [Identity]
    public string Letter { get; set; }
    public List<string> Names { get; set; } = new List<string>();
}

public class NameEvent
{
    public string Name { get; set; }
}

public class NamedDocument
{
    public string Name { get; set; }
    public Guid Id { get; set; }
}
