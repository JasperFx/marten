using System;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests;

/// <summary>
/// marten#6071: <c>Advanced.RebuildSingleStreamAsync</c> for a document an asynchronous single-stream projection
/// writes. It goes through a daemon of this store in this process when one runs the shard, is refused when the
/// shard is live on another node, and otherwise rebuilds offline.
/// </summary>
public class rebuilding_a_single_stream_beside_a_daemon: OneOffConfigurationsContext
{
    private static readonly TimeSpan Generous = 30.Seconds();

    private void configure()
    {
        StoreOptions(opts => opts.Projections.Add<RebuiltRosterProjection>(ProjectionLifecycle.Async));
    }

    private async Task<Guid> aRosterAsync(params object[] events)
    {
        var id = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<RebuiltRoster>(id, events);
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private async Task corruptAsync(Guid id)
    {
        await using var session = theStore.LightweightSession();
        session.Store(new RebuiltRoster { Id = id, Members = 999 });
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<RebuiltRoster?> loadAsync(Guid id)
    {
        await using var query = theStore.QuerySession();
        return await query.LoadAsync<RebuiltRoster>(id, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task goes_through_the_daemon_that_runs_the_shard_in_this_process()
    {
        configure();
        var id = await aRosterAsync(new RosterJoined(), new RosterJoined());

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await theStore.Storage.Database.WaitForNonStaleProjectionDataAsync(Generous);

        await corruptAsync(id);

        // The shard has just written its progression row, so anything but the daemon path would be refused
        await theStore.Advanced.RebuildSingleStreamAsync<RebuiltRoster>(id, TestContext.Current.CancellationToken);

        (await loadAsync(id)).ShouldNotBeNull().Members.ShouldBe(2);

        await daemon.StopAllAsync();
    }

    [Fact]
    public async Task is_refused_when_the_shard_is_live_on_another_node()
    {
        configure();
        var id = await aRosterAsync(new RosterJoined());

        // A second store over the same schema stands in for another node: this store cannot reach its daemon
        var otherNode = SeparateStore(opts => opts.Projections.Add<RebuiltRosterProjection>(ProjectionLifecycle.Async));
        using var daemon = await otherNode.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await otherNode.Storage.Database.WaitForNonStaleProjectionDataAsync(Generous);

        var refused = await Should.ThrowAsync<IdentityRebuildRefusedException>(() =>
            theStore.Advanced.RebuildSingleStreamAsync<RebuiltRoster>(id, TestContext.Current.CancellationToken));

        refused.Reason.ShouldBe(IdentityRebuildRefusal.ShardLiveOnAnotherNode);

        await daemon.StopAllAsync();
    }

    [Fact]
    public async Task rebuilds_offline_when_no_daemon_has_run_the_shard()
    {
        configure();
        var id = await aRosterAsync(new RosterJoined(), new RosterJoined(), new RosterJoined());

        await theStore.Advanced.RebuildSingleStreamAsync<RebuiltRoster>(id, TestContext.Current.CancellationToken);

        (await loadAsync(id)).ShouldNotBeNull().Members.ShouldBe(3);
    }

    [Fact]
    public async Task a_stream_that_folds_to_nothing_deletes_the_document()
    {
        configure();

        // A document left behind for a stream that no longer folds to anything. This used to throw from Store(null).
        var id = await aRosterAsync(new RosterJoined(), new RosterDisbanded());
        await corruptAsync(id);

        await theStore.Advanced.RebuildSingleStreamAsync<RebuiltRoster>(id, TestContext.Current.CancellationToken);

        (await loadAsync(id)).ShouldBeNull();
    }
}

public record RosterJoined;

public record RosterDisbanded;

public class RebuiltRoster
{
    public Guid Id { get; set; }
    public int Members { get; set; }
}

public class RebuiltRosterProjection: SingleStreamProjection<RebuiltRoster, Guid>
{
    public RebuiltRoster Create(RosterJoined _) => new() { Members = 1 };

    public void Apply(RosterJoined _, RebuiltRoster roster) => roster.Members++;

    public bool ShouldDelete(RosterDisbanded _) => true;
}
