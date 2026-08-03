using System;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.Bugs;

/// <summary>
/// #5161: <c>WaitForNonStaleProjectionDataAsync</c>'s store-global bar required EVERY row returned by
/// <c>AllProjectionProgress</c> to reach the initial event sequence. That set is the whole of
/// mt_event_progression, which also holds rows that are not projection shards and have no reason to
/// track the sequence — high-water bookkeeping, and residue from projections that are no longer
/// registered. Nothing advances those, so the wait could never complete and the caller timed out even
/// though every real shard had finished its work.
/// </summary>
public class Bug_5161_wait_ignores_non_shard_progression_rows: OneOffConfigurationsContext
{
    [Fact]
    public async Task lagging_non_shard_rows_do_not_hold_the_wait_open()
    {
        StoreOptions(opts => opts.Projections.Add<Bug5161Projection>(ProjectionLifecycle.Async));

        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        await using (var session = theStore.LightweightSession())
        {
            for (var i = 0; i < 5; i++)
            {
                session.Events.StartStream(Guid.NewGuid(), new Bug5161Event(i));
            }

            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await theStore.WaitForNonStaleProjectionDataAsync(30.Seconds());
            await daemon.StopAllAsync();
        }

        // Two rows that are NOT projection shards and that nothing will ever advance: the high-water
        // allocation fence (#5108 bookkeeping, which legitimately records an older sequence) and a
        // leftover row from a projection that is no longer registered. Both sit far below the mark.
        await insertProgressionRow("HighWaterAllocationFence", 1);
        await insertProgressionRow("SomeRetiredProjection:All", 1);

        // Every real shard is already caught up, so this must return promptly rather than spin until
        // the timeout. A generous-but-finite timeout keeps a regression reported as a failure rather
        // than a hang.
        await Should.NotThrowAsync(async () =>
            await theStore.WaitForNonStaleProjectionDataAsync(10.Seconds()));
    }

    private async Task insertProgressionRow(string name, long sequence)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"insert into {theStore.Events.DatabaseSchemaName}.mt_event_progression (name, last_seq_id, last_updated) values (@name, @seq, transaction_timestamp()) on conflict (name) do update set last_seq_id = @seq";
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("seq", sequence);

        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}

public record Bug5161Event(int Number);

public class Bug5161Doc
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

public partial class Bug5161Projection: EventProjection
{
    public Bug5161Projection()
    {
        Name = "Bug5161";
    }

    public Bug5161Doc Create(IEvent<Bug5161Event> e) =>
        new() { Id = e.StreamId, Count = e.Data.Number };
}
