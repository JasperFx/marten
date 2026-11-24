using System;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Events;
using Marten.Events.Daemon.HighWater;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.Bugs;

/// <summary>
/// #6185: TryCorrectProgressInDatabaseAsync pulls back a high water mark (and any progression) that sits ABOVE the
/// highest assigned event sequence. It used to set EVERY progression row to that sequence, which moved a shard that
/// was behind it forward, so the shard skipped the events in between.
/// </summary>
public class Bug_6185_correct_progress_leaves_lagging_shards: DaemonContext
{
    public Bug_6185_correct_progress_leaves_lagging_shards(ITestOutputHelper output): base(output)
    {
    }

    private string Schema => theStore.Events.DatabaseSchemaName;

    private async Task execute(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await conn.CreateCommand(sql).ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<long> progressionOf(string name)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(
            $"select last_seq_id from {Schema}.mt_event_progression where name = $1", conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = name });
        var raw = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return Convert.ToInt64(raw);
    }

    [Fact]
    public async Task a_shard_behind_the_highest_sequence_keeps_its_position()
    {
        await using (var session = theStore.LightweightSession())
        {
            for (var i = 0; i < 10; i++)
            {
                session.Events.StartStream(Guid.NewGuid(), new Bug6185Event(i));
            }

            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var lastEvent = (await theStore.Advanced.FetchEventStoreStatistics(
            token: TestContext.Current.CancellationToken)).EventSequenceNumber;
        lastEvent.ShouldBeGreaterThanOrEqualTo(10);

        var lagging = lastEvent - 5;
        await execute($"select {Schema}.mt_mark_event_progression('{HighWaterShardIdentity.StoreGlobal}', {lastEvent + 1000})");
        await execute($"select {Schema}.mt_mark_event_progression('Bug6185Ahead:All', {lastEvent + 500})");
        await execute($"select {Schema}.mt_mark_event_progression('Bug6185Lagging:All', {lagging})");

        await theStore.Advanced.TryCorrectProgressInDatabaseAsync(TestContext.Current.CancellationToken);

        // The mark and the shard that claimed unassigned events are pulled back to the highest sequence
        var corrected = await progressionOf(HighWaterShardIdentity.StoreGlobal);
        corrected.ShouldBeGreaterThanOrEqualTo(lastEvent);
        corrected.ShouldBeLessThan(lastEvent + 500);
        (await progressionOf("Bug6185Ahead:All")).ShouldBe(corrected);

        // The shard behind it still has events to process, and keeps its place
        (await progressionOf("Bug6185Lagging:All")).ShouldBe(lagging);
    }
}

public record Bug6185Event(int Number);
