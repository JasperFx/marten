using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DaemonTests;

/// <summary>
/// #6004: read-your-writes. <c>session.LastAppendedPosition</c> says where a commit's events are, and
/// <c>store.WaitForProjectionsAsync</c> waits for named projections to process that position, and nothing past
/// it, by polling <c>mt_event_progression</c>.
/// </summary>
public class read_your_writes: OneOffConfigurationsContext
{
    private static readonly TimeSpan Generous = 30.Seconds();

    private void configure(EventAppendMode mode = EventAppendMode.Rich, Action<StoreOptions>? more = null)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = mode;
            opts.Projections.Add<RywTallyProjection>(ProjectionLifecycle.Async);
            more?.Invoke(opts);
        });
    }

    private async Task<EventStorePosition> appendAsync(int count, bool fail = false)
    {
        await using var session = theStore.LightweightSession();
        session.Events.StartStream(Guid.NewGuid(),
            Enumerable.Range(0, count).Select(i => (object)new RywCounted(fail && i == count - 1)).ToArray());
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        return session.LastAppendedPosition.ShouldNotBeNull();
    }

    private async Task<long> highestSequenceAsync()
    {
        return await scalarAsync($"select coalesce(max(seq_id), 0) from {SchemaName}.mt_events");
    }

    private async Task<long> scalarAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private async Task executeAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private Task markProgressAsync(string name, long sequence)
        => executeAsync(
            $"insert into {SchemaName}.mt_event_progression (name, last_seq_id, last_updated) values ('{name}', {sequence}, transaction_timestamp()) on conflict (name) do update set last_seq_id = {sequence}");

    [Theory]
    [InlineData(EventAppendMode.Rich)]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task the_session_records_the_highest_sequence_a_commit_appended(EventAppendMode mode)
    {
        configure(mode);

        await using var session = theStore.LightweightSession();
        session.LastAppendedPosition.ShouldBeNull();

        // Documents only: nothing appended, so no position
        session.Store(new RywNote { Id = Guid.NewGuid() });
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        session.LastAppendedPosition.ShouldBeNull();

        session.Events.StartStream(Guid.NewGuid(), new RywCounted(false), new RywCounted(false));
        session.Events.StartStream(Guid.NewGuid(), new RywCounted(false));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        var first = session.LastAppendedPosition.ShouldNotBeNull();
        first.Sequence.ShouldBe(await highestSequenceAsync());
        first.DatabaseIdentifier.ShouldBe(theStore.Storage.Database.Identifier);
        first.TenantId.ShouldBe(session.TenantId);

        session.Events.StartStream(Guid.NewGuid(), new RywCounted(false));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        var second = session.LastAppendedPosition.ShouldNotBeNull();
        second.Sequence.ShouldBe(await highestSequenceAsync());
        second.Sequence.ShouldBeGreaterThan(first.Sequence);

        // Another commit with no events keeps the last position
        session.Store(new RywNote { Id = Guid.NewGuid() });
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        session.LastAppendedPosition.ShouldBe(second);
    }

    [Fact]
    public async Task reached_once_the_projection_passes_the_position_seen_from_another_node()
    {
        configure();
        var position = await appendAsync(3);

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        // A second store over the same schema runs no daemon, which is what any other node looks like
        var otherNode = SeparateStore(opts => opts.Projections.Add<RywTallyProjection>(ProjectionLifecycle.Async));

        var result = await otherNode.WaitForProjectionsAsync(position, [typeof(RywTally)], Generous,
            TestContext.Current.CancellationToken);

        result.Reached.ShouldBeTrue(result.ToString());
        result.Shards.Single().Sequence!.Value.ShouldBeGreaterThanOrEqualTo(position.Sequence);

        await using var query = otherNode.QuerySession();
        (await query.LoadAsync<RywTally>("tally", TestContext.Current.CancellationToken))!.Count.ShouldBe(3);
    }

    [Fact]
    public async Task wait_by_projection_class_or_by_name()
    {
        configure();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var store = theStore;

        #region sample_read_your_writes

        await using var session = store.LightweightSession();
        session.Events.StartStream(Guid.NewGuid(), new RywCounted(false));
        await session.SaveChangesAsync();

        // Where this commit's events are: database, tenant and sequence
        var position = session.LastAppendedPosition!;

        var result = await store.WaitForProjectionsAsync(position, [typeof(RywTally)], 5.Seconds());
        if (result.Reached)
        {
            var tally = await session.LoadAsync<RywTally>("tally");
        }
        else
        {
            // Timed out, or the projection is paused or rebuilding. The write succeeded either way,
            // and result.Shards says how far each shard got
        }

        #endregion

        result.Reached.ShouldBeTrue(result.ToString());

        (await theStore.WaitForProjectionsAsync(position, [typeof(RywTallyProjection)], Generous,
            TestContext.Current.CancellationToken)).Reached.ShouldBeTrue();

        (await theStore.WaitForProjectionsAsync(position, [RywTallyProjection.ProjectionName], Generous,
            TestContext.Current.CancellationToken)).Reached.ShouldBeTrue();
    }

    [Fact]
    public async Task a_busy_store_does_not_hold_the_caller()
    {
        configure();
        var position = await appendAsync(2);

        // Another writer commits after the caller, and the projection has only got as far as the caller
        await appendAsync(50);
        await markProgressAsync($"{RywTallyProjection.ProjectionName}:All", position.Sequence);

        (await highestSequenceAsync()).ShouldBeGreaterThan(position.Sequence);

        var result = await theStore.WaitForProjectionsAsync(position, [typeof(RywTally)], 5.Seconds(),
            TestContext.Current.CancellationToken);

        result.Reached.ShouldBeTrue(result.ToString());
        result.Elapsed.ShouldBeLessThan(5.Seconds());

        // The store-wide wait would still be waiting for the other writer's events
        await Should.ThrowAsync<TimeoutException>(() =>
            theStore.Storage.Database.WaitForNonStaleProjectionDataAsync(typeof(RywTally), 500.Milliseconds(),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task a_sequence_gap_below_the_callers_events_holds_the_wait_until_gap_detection_clears_it()
    {
        configure(more: opts => opts.Projections.StaleSequenceThreshold = 1.Seconds());
        theStore.EnsureStorageExists(typeof(IEvent));

        await appendAsync(2);

        // A sequence taken and never used, as a rolled back append leaves behind
        await scalarAsync($"select nextval('{SchemaName}.mt_events_sequence')");

        var position = await appendAsync(2);

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var result = await theStore.WaitForProjectionsAsync(position, [typeof(RywTally)], Generous,
            TestContext.Current.CancellationToken);

        result.Reached.ShouldBeTrue(result.ToString());
    }

    [Fact]
    public async Task a_timeout_is_a_result_with_how_far_each_shard_got()
    {
        configure();
        var position = await appendAsync(5);

        // No daemon is running, so nothing has a row yet
        var nothing = await theStore.WaitForProjectionsAsync(position, [typeof(RywTally)], 300.Milliseconds(),
            TestContext.Current.CancellationToken);

        nothing.TimedOut.ShouldBeTrue();
        nothing.Reached.ShouldBeFalse();
        nothing.Shards.Single().Sequence.ShouldBeNull();

        await markProgressAsync($"{RywTallyProjection.ProjectionName}:All", position.Sequence - 2);

        var behind = await theStore.WaitForProjectionsAsync(position, [typeof(RywTally)], 300.Milliseconds(),
            TestContext.Current.CancellationToken);

        behind.TimedOut.ShouldBeTrue();
        behind.Shards.Single().Sequence.ShouldBe(position.Sequence - 2);
        behind.Shards.Single().Shard.Identity.ShouldBe($"{RywTallyProjection.ProjectionName}:All");
    }

    [Fact]
    public async Task a_paused_projection_returns_at_once_on_any_node()
    {
        configure(more: opts =>
        {
            opts.Projections.Errors.SkipApplyErrors = false;
            opts.Events.EnableExtendedProgressionTracking = true;
        });

        // The projection has to have a progression row for the pause to be recorded on
        var good = await appendAsync(2);

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        (await theStore.WaitForProjectionsAsync(good, [typeof(RywTally)], Generous,
            TestContext.Current.CancellationToken)).Reached.ShouldBeTrue();

        var position = await appendAsync(1, fail: true);

        // The other node can only see the pause through the progression row
        var otherNode = SeparateStore(opts =>
        {
            opts.Projections.Add<RywTallyProjection>(ProjectionLifecycle.Async);
            opts.Events.EnableExtendedProgressionTracking = true;
        });

        var result = await otherNode.WaitForProjectionsAsync(position, [typeof(RywTally)], Generous,
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(ProjectionWaitStatus.Paused, result.ToString());
        result.Shards.Single().Paused.ShouldBeTrue();
        result.Elapsed.ShouldBeLessThan(Generous);
    }

    [Fact]
    public async Task a_projection_this_node_is_rebuilding_returns_at_once()
    {
        configure();
        var position = await appendAsync(3);
        await markProgressAsync($"{RywTallyProjection.ProjectionName}:All", 1);

        // What a rebuilding agent publishes to its database's tracker (jasperfx#681). Marten does not persist the
        // mode, so only the node running the rebuild can see it.
        var tracker = ((IEventDatabase)theStore.Storage.Database).Tracker;
        ((IObserver<ShardState>)tracker).OnNext(
            new ShardState($"{RywTallyProjection.ProjectionName}:All", 1) { Mode = ShardMode.rebuilding });

        var result = await theStore.WaitForProjectionsAsync(position, [typeof(RywTally)], Generous,
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(ProjectionWaitStatus.Rebuilding, result.ToString());
        result.Elapsed.ShouldBeLessThan(5.Seconds());
    }

    [Fact]
    public async Task a_type_no_asynchronous_projection_writes_is_refused()
    {
        configure();
        var position = await appendAsync(1);

        await Should.ThrowAsync<ArgumentException>(() =>
            theStore.WaitForProjectionsAsync(position, [typeof(RywNote)], Generous,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void the_position_travels_as_text()
    {
        #region sample_event_store_position_as_header

        var position = new EventStorePosition("main", "tenant-a", 1234);

        // "main/tenant-a/1234", safe to put in a header
        var header = position.ToString();

        var back = EventStorePosition.Parse(header);

        #endregion

        back.ShouldBe(position);
    }
}

public record RywCounted(bool Fail);

public class RywNote
{
    public Guid Id { get; set; }
}

public class RywTally
{
    public string Id { get; set; } = "tally";
    public int Count { get; set; }
}

public partial class RywTallyProjection: MultiStreamProjection<RywTally, string>
{
    public const string ProjectionName = "RywTally";

    public RywTallyProjection()
    {
        Name = ProjectionName;
        Identity<RywCounted>(_ => "tally");
    }

    public void Apply(RywCounted e, RywTally tally)
    {
        if (e.Fail) throw new InvalidOperationException("RywCounted asked to fail");
        tally.Count++;
    }
}
