using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// The Async fetch scenarios that <see cref="async_fetch_reads_in_a_safe_order" /> does not run per store
/// setup: several fetches racing in one batch, <c>FetchManyForWriting</c>, compacted streams, a snapshot
/// that goes backwards on every attempt, and writing through a handle that saw an append mid-fetch.
/// </summary>
public class async_fetch_edge_cases: OneOffConfigurationsContext
{
    public async_fetch_edge_cases()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
            opts.Events.UseIdentityMapForAggregates = false;
        });
    }

    private async Task catchTheDaemonUpAsync()
    {
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.CatchUpAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> aStreamAsync(params object[] events)
    {
        var id = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<SimpleAggregate>(id, events);
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private Task<Guid> aStreamOfABCAsync() => aStreamAsync(new AEvent(), new BEvent(), new CEvent());

    private async Task executeAsync(string sql, Guid id)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private Task deleteTheSnapshotAsync(Guid id) =>
        executeAsync($"delete from {SchemaName}.mt_doc_simpleaggregate where id = @id", id);

    private static void shouldBeTheWholeStream(SimpleAggregate? aggregate)
    {
        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(1);
    }

    /// <summary>Waits until some session in this database is blocked on a lock.</summary>
    private static async Task aSessionIsWaitingOnALockAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select exists (select 1 from pg_stat_activity " +
                          "where datname = current_database() and wait_event_type = 'Lock')";

        var giveUp = DateTime.UtcNow.AddSeconds(30);
        while (!(bool)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!)
        {
            if (DateTime.UtcNow > giveUp) throw new TimeoutException("No session ever waited on a lock");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private async Task theLockIsHeldUntilSaveChangesAsync(IDocumentSession session, IEventStream<SimpleAggregate> locked)
    {
        await using var other = theStore.LightweightSession();
        var blocked = other.Events.FetchForExclusiveWriting<SimpleAggregate>(locked.Id,
            TestContext.Current.CancellationToken);

        await aSessionIsWaitingOnALockAsync();
        blocked.IsCompleted.ShouldBeFalse();

        locked.AppendOne(new DEvent());
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        (await blocked).Aggregate!.DCount.ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task one_of_several_fetches_in_a_batch_with_an_exclusive_one_races_the_daemon(int racingFetch)
    {
        // Enlisted as: an exclusive fetch, a FetchForWriting, a FetchLatest, with a Load between them.
        // The held one straddles the daemon's write; the others read before or after it.
        var lockedId = await aStreamOfABCAsync();
        var writingId = await aStreamOfABCAsync();
        var latestId = await aStreamOfABCAsync();

        await using var session = theStore.LightweightSession();
        var pause = new async_fetch_reads_in_a_safe_order.PauseTheFirstRead(catchTheDaemonUpAsync,
            fetchIndex: racingFetch);
        session.Logger = pause;

        var batch = session.CreateBatchQuery();
        var exclusive = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(lockedId);
        var load = batch.Load<Target>(Guid.NewGuid());
        var writing = batch.Events.FetchForWriting<SimpleAggregate>(writingId);
        var latest = batch.Events.FetchLatest<SimpleAggregate>(latestId);
        await batch.Execute(TestContext.Current.CancellationToken);
        await pause.Paused;

        (await load).ShouldBeNull();
        shouldBeTheWholeStream((await writing).Aggregate);
        (await writing).CurrentVersion.ShouldBe(3);
        shouldBeTheWholeStream(await latest);

        var locked = await exclusive;
        shouldBeTheWholeStream(locked.Aggregate);
        locked.CurrentVersion.ShouldBe(3);

        await theLockIsHeldUntilSaveChangesAsync(session, locked);
    }

    [Fact]
    public async Task a_refused_batch_holding_an_exclusive_fetch_releases_its_lock_with_the_session()
    {
        var lockedId = await aStreamOfABCAsync();
        var racingId = await aStreamOfABCAsync();
        await catchTheDaemonUpAsync();

        await using (var session = theStore.LightweightSession())
        {
            session.Logger = new async_fetch_reads_in_a_safe_order.PauseTheFirstRead(
                () => deleteTheSnapshotAsync(racingId), fetchIndex: 1);

            var batch = session.CreateBatchQuery();
            var exclusive = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(lockedId);
            _ = batch.Events.FetchForWriting<SimpleAggregate>(racingId);

            var ex = await Should.ThrowAsync<SnapshotRaceException>(() =>
                batch.Execute(TestContext.Current.CancellationToken));
            ex.Id.ShouldBe(racingId);

            // The exclusive fetch was read before the failing one, so it did come back
            (await exclusive).CurrentVersion.ShouldBe(3);
        }

        // Disposing the session rolled its transaction back, so the stream can be locked again at once
        await using var other = theStore.LightweightSession();
        var stream = await other.Events.FetchForExclusiveWriting<SimpleAggregate>(lockedId,
            TestContext.Current.CancellationToken).WaitAsync(5.Seconds(), TestContext.Current.CancellationToken);
        stream.CurrentVersion.ShouldBe(3);
    }

    [Fact]
    public async Task fetch_many_for_writing_reads_every_stream_in_one_round_trip_while_the_daemon_writes()
    {
        var ids = new[] { await aStreamOfABCAsync(), await aStreamOfABCAsync(), await aStreamOfABCAsync() };

        await using var session = theStore.LightweightSession();
        var pause = new async_fetch_reads_in_a_safe_order.PauseTheFirstRead(catchTheDaemonUpAsync);
        session.Logger = pause;

        var streams = await session.Events.FetchManyForWriting<SimpleAggregate>(ids,
            TestContext.Current.CancellationToken);
        await pause.Paused;

        pause.FetchBatches.ShouldBe(1);
        streams.Count.ShouldBe(3);
        for (var i = 0; i < ids.Length; i++)
        {
            streams[i].Id.ShouldBe(ids[i]);
            streams[i].CurrentVersion.ShouldBe(3);
            shouldBeTheWholeStream(streams[i].Aggregate);
        }
    }

    [Fact]
    public async Task fetch_many_for_writing_falls_back_to_one_stream_at_a_time_when_a_snapshot_goes_backwards()
    {
        var ids = new[] { await aStreamOfABCAsync(), await aStreamOfABCAsync(), await aStreamOfABCAsync() };
        await catchTheDaemonUpAsync();

        await using var session = theStore.LightweightSession();
        var pause = new async_fetch_reads_in_a_safe_order.PauseTheFirstRead(() => deleteTheSnapshotAsync(ids[0]));
        session.Logger = pause;

        var streams = await session.Events.FetchManyForWriting<SimpleAggregate>(ids,
            TestContext.Current.CancellationToken);
        await pause.Paused;

        // The batch, then one fetch per stream
        pause.FetchBatches.ShouldBe(1 + ids.Length);
        for (var i = 0; i < ids.Length; i++)
        {
            streams[i].Id.ShouldBe(ids[i]);
            streams[i].CurrentVersion.ShouldBe(3);
            shouldBeTheWholeStream(streams[i].Aggregate);
        }
    }

    // Compaction replaces the events up to a version with one Compacted event at that version, which leaves a
    // gap in the event sequence. The daemon does not move past a gap while an older read is still running, so
    // it can only catch a compacted stream's snapshot up mid-fetch once that gap is behind it.

    [Fact]
    public async Task a_compacted_stream_is_fetched_whole_while_the_daemon_writes()
    {
        // The daemon skips a gap only once it is older than the stale-sequence threshold
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
            opts.Events.UseIdentityMapForAggregates = false;
            opts.Projections.StaleSequenceThreshold = 100.Milliseconds();
        });

        var id = await aStreamAsync(new AEvent(), new BEvent(), new CEvent(), new DEvent());
        await using (var session = theStore.LightweightSession())
        {
            await session.Events.CompactStreamAsync<SimpleAggregate>(id, x => x.Version = 3);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await catchTheDaemonUpAsync();

        await using var fetchSession = theStore.LightweightSession();
        var pause = new async_fetch_reads_in_a_safe_order.PauseTheFirstRead(async () =>
        {
            await using (var appending = theStore.LightweightSession())
            {
                appending.Events.Append(id, new EEvent());
                await appending.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await catchTheDaemonUpAsync();
        });
        fetchSession.Logger = pause;

        var stream = await fetchSession.Events.FetchForWriting<SimpleAggregate>(id, TestContext.Current.CancellationToken);
        await pause.Paused;

        stream.CurrentVersion.ShouldBe(5);
        shouldBeTheWholeStream(stream.Aggregate);
        stream.Aggregate!.DCount.ShouldBe(1);
        stream.Aggregate.ECount.ShouldBe(1);
    }

    /// <summary>
    /// Makes the snapshot go backwards on every attempt: before each fetch batch it puts the snapshot back,
    /// and while the first read is held it deletes it again.
    /// </summary>
    private async Task<async_fetch_reads_in_a_safe_order.PauseTheFirstRead> aSnapshotThatGoesBackwardsEveryTimeAsync(
        Guid id)
    {
        var table = $"{SchemaName}.mt_doc_simpleaggregate";
        var backup = $"{SchemaName}.snapshot_backup";
        await executeAsync($"create table {backup} as select * from {table} where id = @id", id);

        return new async_fetch_reads_in_a_safe_order.PauseTheFirstRead(() => deleteTheSnapshotAsync(id),
            pauses: int.MaxValue)
        {
            BeforeEachFetchBatch = () =>
            {
                using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
                conn.Open();
                using var restore = conn.CreateCommand();
                restore.CommandText = $"insert into {table} select * from {backup} on conflict do nothing";
                restore.ExecuteNonQuery();
            }
        };
    }

    [Fact]
    public async Task a_fetch_whose_snapshot_goes_backwards_every_time_gives_up_detecting_after_two_redos()
    {
        var id = await aStreamOfABCAsync();
        await catchTheDaemonUpAsync();

        await using var session = theStore.LightweightSession();
        var pause = await aSnapshotThatGoesBackwardsEveryTimeAsync(id);
        session.Logger = pause;

        // The third attempt runs with detection off and returns instead of throwing
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(id, TestContext.Current.CancellationToken);
        await pause.Paused;

        pause.FetchBatches.ShouldBe(3);
        stream.CurrentVersion.ShouldBe(3);
    }

    [Fact]
    public async Task a_batched_fetch_whose_snapshot_goes_backwards_is_refused_without_a_redo()
    {
        var id = await aStreamOfABCAsync();
        await catchTheDaemonUpAsync();

        await using var session = theStore.LightweightSession();
        var pause = await aSnapshotThatGoesBackwardsEveryTimeAsync(id);
        session.Logger = pause;

        var batch = session.CreateBatchQuery();
        _ = batch.Events.FetchForWriting<SimpleAggregate>(id);
        await Should.ThrowAsync<SnapshotRaceException>(() => batch.Execute(TestContext.Current.CancellationToken));
        await pause.Paused;

        pause.FetchBatches.ShouldBe(1);
    }

    [Fact]
    public async Task the_stream_handle_from_a_fetch_that_saw_an_append_writes_at_the_newer_version()
    {
        var id = await aStreamOfABCAsync();
        await catchTheDaemonUpAsync();

        await using var session = theStore.LightweightSession();
        var pause = new async_fetch_reads_in_a_safe_order.PauseTheFirstRead(async () =>
        {
            await using (var appending = theStore.LightweightSession())
            {
                appending.Events.Append(id, new DEvent());
                await appending.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await catchTheDaemonUpAsync();
        });
        session.Logger = pause;

        var stream = await session.Events.FetchForWriting<SimpleAggregate>(id, TestContext.Current.CancellationToken);
        await pause.Paused;
        stream.CurrentVersion.ShouldBe(4);

        stream.AppendOne(new EEvent());
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var query = theStore.LightweightSession();
        (await query.Events.FetchStreamStateAsync(id, TestContext.Current.CancellationToken))!.Version.ShouldBe(5);
    }
}
