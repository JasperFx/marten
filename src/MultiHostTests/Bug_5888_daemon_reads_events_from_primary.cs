using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Storage;
using Npgsql;
using Shouldly;
using Weasel.Postgresql.SqlGeneration;

namespace MultiHostTests;

/// <summary>
/// #5888: with ReadSessionPreference pointing at a standby, the async daemon took its high-water mark
/// from the PRIMARY but loaded event pages through a QuerySession, which routes to the STANDBY. Under
/// replication lag the standby returned a short page, a short page reports the high-water mark as its
/// ceiling, and progression was written past events the standby had not replayed yet. Those events
/// were never projected: no exception, no dead letter, and a shard reporting itself caught up.
///
/// Lag is made deterministic by pausing WAL replay on the standby (pg_wal_replay_pause) after the
/// first event has replicated, so everything appended afterwards exists on the primary only.
/// </summary>
public class Bug_5888_daemon_reads_events_from_primary: MultiHostConfigurationContext
{
    [Fact]
    public async Task async_projection_sees_every_event_while_the_standby_lags()
    {
        StoreOptions(opts => opts.Projections.Snapshot<TickCounter>(SnapshotLifecycle.Async));

        var streamId = Guid.NewGuid();
        await AppendEvent(streamId, new Tick());

        await waitForStandbyToCatchUpAsync();
        await pauseStandbyReplayAsync();
        try
        {
            // Committed on the primary, invisible to the standby until replay resumes
            await AppendEvent(streamId, new Tick(), new Tick(), new Tick(), new Tick(), new Tick(), new Tick(),
                new Tick(), new Tick(), new Tick());

            using var daemon = await theStore.BuildProjectionDaemonAsync();
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(30.Seconds());

            // A write session reads from the primary, where the projection writes its documents
            await using var session = theStore.LightweightSession();
            var counter = await session.LoadAsync<TickCounter>(streamId);

            counter.ShouldNotBeNull();
            counter.Count.ShouldBe(10);
        }
        finally
        {
            await resumeStandbyReplayAsync();
        }
    }

    public enum LoadStrategy
    {
        Normal,
        SkipAhead,
        WindowStep
    }

    [Theory]
    [InlineData(LoadStrategy.Normal)]
    [InlineData(LoadStrategy.SkipAhead)]
    [InlineData(LoadStrategy.WindowStep)]
    public async Task event_loader_reads_from_the_primary_while_the_standby_lags(LoadStrategy strategy)
    {
        StoreOptions(_ => { });

        var streamId = Guid.NewGuid();
        await AppendEvent(streamId, new Tick());

        await waitForStandbyToCatchUpAsync();
        await pauseStandbyReplayAsync();
        try
        {
            await AppendEvent(streamId, new Tick(), new Tick(), new Tick(), new Tick());

            var highWater = await fetchHighestSequenceOnPrimaryAsync();

            var filters = new ISqlFragment[] { new EventTypeFilter(theStore.Events, new[] { typeof(Tick) }) };
            var loader = new EventLoader(theStore, (MartenDatabase)theStore.Tenancy.Default.Database,
                new AsyncOptions { BatchSize = 500 }, filters);

            var request = new EventRequest
            {
                Floor = 0,
                HighWater = highWater,
                BatchSize = 500,
                ErrorOptions = new ErrorHandlingOptions(),
                Runtime = new NulloDaemonRuntime(),
                Name = new ShardName("TickCounter", "All", 1)
            };

            var page = strategy switch
            {
                LoadStrategy.Normal => await loader.LoadAsync(request, CancellationToken.None),
                LoadStrategy.SkipAhead => await loader.LoadWithSkipAheadAsync(request, CancellationToken.None),
                _ => await loader.LoadWithWindowStepAsync(request, CancellationToken.None)
            };

            // Pre-fix the standby answered: one event, yet a ceiling of highWater, so the other four
            // would have been recorded as processed without ever being loaded
            page.Count.ShouldBe(5);
            page.Ceiling.ShouldBe(highWater);
        }
        finally
        {
            await resumeStandbyReplayAsync();
        }
    }

    private async Task<long> fetchHighestSequenceOnPrimaryAsync()
    {
        await using var conn = theDataSource.CreateConnection(TargetSessionAttributes.Primary);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"select max(seq_id) from {theStore.Options.Events.DatabaseSchemaName}.mt_events", conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task waitForStandbyToCatchUpAsync()
    {
        string primaryLsn;
        await using (var primary = theDataSource.CreateConnection(TargetSessionAttributes.Primary))
        {
            await primary.OpenAsync();
            await using var cmd = new NpgsqlCommand("select pg_current_wal_lsn()::text", primary);
            primaryLsn = (string)(await cmd.ExecuteScalarAsync())!;
        }

        await using var standby = theDataSource.CreateConnection(TargetSessionAttributes.Standby);
        await standby.OpenAsync();
        await using var check = new NpgsqlCommand("select pg_last_wal_replay_lsn() >= @lsn::pg_lsn", standby);
        check.Parameters.AddWithValue("lsn", primaryLsn);

        for (var attempt = 0; attempt < 150; attempt++)
        {
            if ((bool)(await check.ExecuteScalarAsync())!) return;
            await Task.Delay(100);
        }

        throw new TimeoutException($"The standby did not replay up to {primaryLsn} within 15 seconds");
    }

    private async Task pauseStandbyReplayAsync()
    {
        await using var standby = theDataSource.CreateConnection(TargetSessionAttributes.Standby);
        await standby.OpenAsync();

        await using (var pause = new NpgsqlCommand("select pg_wal_replay_pause()", standby))
        {
            await pause.ExecuteNonQueryAsync();
        }

        // pg_wal_replay_pause only REQUESTS the pause; replay may still apply in-flight WAL until the
        // state reads 'paused'
        await using var state = new NpgsqlCommand("select pg_get_wal_replay_pause_state()", standby);
        for (var attempt = 0; attempt < 150; attempt++)
        {
            if ((string)(await state.ExecuteScalarAsync())! == "paused") return;
            await Task.Delay(100);
        }

        throw new TimeoutException("WAL replay on the standby did not pause within 15 seconds");
    }

    private async Task resumeStandbyReplayAsync()
    {
        await using var standby = theDataSource.CreateConnection(TargetSessionAttributes.Standby);
        await standby.OpenAsync();
        await using var resume = new NpgsqlCommand("select pg_wal_replay_resume()", standby);
        await resume.ExecuteNonQueryAsync();
    }
}

public class Tick;

public class TickCounter
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public void Apply(Tick _) => Count++;
}
