using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Testing.Harness;
using Npgsql;
using NpgsqlTypes;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #5539: <c>mt_quick_append_events</c> called with ZERO events used to write the version it read at the
/// top of the function back over a concurrent appender's, leaving <c>mt_streams.version</c> BELOW the
/// stream's own events — and from then on every append computed a version that already existed and failed
/// on <c>pk_mt_events_stream_and_version</c>, permanently, until the row was repaired by hand.
/// </summary>
/// <remarks>
/// <para>
/// The interleaving is arranged rather than hoped for, and it is arranged at the level the bug lives at —
/// two raw connections calling the function directly, because no current Marten code path makes the
/// zero-event call. <c>QuickEventAppender</c> only invokes the function for streams that have events, and
/// the two callers that used to pass empty arrays were both fixed (#5062, #5262). The hazard was in the
/// FUNCTION, live for any caller current or future, which is why the fix and this test are both aimed
/// there.
/// </para>
/// <para>
/// Why zero events and not one: with events, the loser's first INSERT collides with the winner on
/// <c>pk_mt_events_stream_and_version</c> and the whole transaction rolls back. With no events there is no
/// INSERT to collide, so nothing stops the trailing blind <c>UPDATE</c> — which under READ COMMITTED waits
/// on the winner's row lock, then re-checks its WHERE clause against the winner's committed row and
/// applies the stale value anyway. That was why the empty case was the exposed one.
/// </para>
/// <para>
/// Since #6066 the function locks the stream row when it reads the version, so a concurrent caller waits
/// there and reads the winner's committed version, with or without events.
/// <see cref="a_concurrent_single_event_append_waits_and_lands_after_the_first"/> pins that.
/// </para>
/// </remarks>
public class Bug_5539_quick_append_with_zero_events : OneOffConfigurationsContext
{
    public record Ping(int N);

    private async Task configureAsync()
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.AddEventType(typeof(Ping));

            // quickAppendCommand() calls mt_quick_append_events directly with the signature that has no
            // metadata arrays, so keep the event metadata off (it is on by default since 10.0)
            opts.Events.MetadataConfig.CausationIdEnabled = false;
            opts.Events.MetadataConfig.CorrelationIdEnabled = false;
            opts.Events.MetadataConfig.HeadersEnabled = false;
            opts.Events.MetadataConfig.UserNameEnabled = false;
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    [Fact]
    public async Task a_concurrent_zero_event_append_cannot_lower_the_stream_version()
    {
        await configureAsync();

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new Ping(1), new Ping(2), new Ping(3));
            await session.SaveChangesAsync();
        }

        (await readVersionsAsync(streamId)).ShouldBe((3, 3));

        // Writer A appends two events through the real function and holds its transaction open, so its
        // trailing UPDATE of mt_streams is committed-pending and holds the row lock.
        await using var a = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await a.OpenAsync();
        var txA = await a.BeginTransactionAsync();
        await using (var cmd = quickAppendCommand(a, txA, streamId, 2))
        {
            await cmd.ExecuteScalarAsync();
        }

        // Writer B makes a ZERO-event call on the same stream in its own transaction. Its read sees the
        // committed version 3, because A has not committed.
        await using var b = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await b.OpenAsync();
        int backendB;
        await using (var pid = new NpgsqlCommand("select pg_backend_pid()", b))
        {
            backendB = (int)(await pid.ExecuteScalarAsync())!;
        }

        var txB = await b.BeginTransactionAsync();
        var bTask = Task.Run(async () =>
        {
            await using var cmd = quickAppendCommand(b, txB, streamId, 0);
            return (long[])(await cmd.ExecuteScalarAsync())!;
        });

        // Do not leave the interleaving to chance: wait until B has demonstrably got past its own read,
        // which is true either because it is now blocked on A's row lock (the unfixed function, whose
        // trailing UPDATE has to wait) or because the whole call has returned (the fixed function, which
        // issues no UPDATE at all for an empty append). Waiting on only one of the two would hang for the
        // full timeout against the other, and committing A without waiting at all would let B read the
        // POST-commit version and pass vacuously.
        var reached = await waitUntilBlockedOrDoneAsync(backendB, bTask);
        reached.ShouldBeTrue("Writer B neither blocked on the row lock nor completed, so the interleaving this test needs was never established");

        await txA.CommitAsync();
        (await readVersionsAsync(streamId)).ShouldBe((5, 5));

        await bTask;
        await txB.CommitAsync();

        var (streamVersion, maxEventVersion) = await readVersionsAsync(streamId);

        // The whole bug in one assertion: before #5539 this was (3, 5).
        streamVersion.ShouldBe(maxEventVersion);
        streamVersion.ShouldBe(5);

        // …and the consequence a production stream actually suffered: the next ordinary append computes
        // event_version + 1 from the stream row, which for a lowered row is a version that already exists.
        await using var after = theStore.LightweightSession();
        after.Events.Append(streamId, new Ping(6));
        await after.SaveChangesAsync();

        (await readVersionsAsync(streamId)).ShouldBe((6, 6));
    }

    [Fact]
    public async Task a_concurrent_single_event_append_waits_and_lands_after_the_first()
    {
        await configureAsync();

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new Ping(1), new Ping(2), new Ping(3));
            await session.SaveChangesAsync();
        }

        await using var a = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await a.OpenAsync();
        var txA = await a.BeginTransactionAsync();
        await using (var cmd = quickAppendCommand(a, txA, streamId, 2))
        {
            await cmd.ExecuteScalarAsync();
        }

        await using var b = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await b.OpenAsync();
        int backendB;
        await using (var pid = new NpgsqlCommand("select pg_backend_pid()", b))
        {
            backendB = (int)(await pid.ExecuteScalarAsync())!;
        }

        var txB = await b.BeginTransactionAsync();
        var bTask = Task.Run(async () =>
        {
            await using var cmd = quickAppendCommand(b, txB, streamId, 1);
            return (long[])(await cmd.ExecuteScalarAsync())!;
        });

        // The same discipline the zero-event test needs, and for the same reason: committing A without
        // waiting lets B's read see the POST-commit version and the wait this test is about never happens.
        // Since #6066 B blocks at the function's first statement, the version read, which locks the stream
        // row A holds. Before #6066 it read version 3 unlocked, inserted version 4 on top of A's uncommitted
        // tuple for that primary key, and failed with a unique violation once A committed.
        var reached = await waitUntilBlockedOrDoneAsync(backendB, bTask);
        reached.ShouldBeTrue("Writer B never blocked on writer A, so the interleaving this test needs was never established");

        await txA.CommitAsync();

        // B read the version A committed, so its event goes in after A's
        var versions = await bTask;
        versions[0].ShouldBe(6);
        await txB.CommitAsync();

        (await readVersionsAsync(streamId)).ShouldBe((6, 6));
    }

    private NpgsqlCommand quickAppendCommand(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid streamId, int eventCount)
    {
        var schema = theStore.Options.Events.DatabaseSchemaName;
        var cmd = new NpgsqlCommand(
            $"select {schema}.mt_quick_append_events(@stream, null, '*DEFAULT*', @ids, @types, @dotnet, @bodies, @bdatas)",
            conn, tx);

        cmd.Parameters.AddWithValue("stream", streamId);
        cmd.Parameters.AddWithValue("ids", Enumerable.Range(0, eventCount).Select(_ => Guid.NewGuid()).ToArray());
        cmd.Parameters.AddWithValue("types", Enumerable.Repeat("ping", eventCount).ToArray());
        cmd.Parameters.AddWithValue("dotnet", Enumerable.Repeat(typeof(Ping).FullName!, eventCount).ToArray());
        cmd.Parameters.Add(new NpgsqlParameter("bodies", NpgsqlDbType.Array | NpgsqlDbType.Jsonb)
        {
            Value = Enumerable.Repeat("{}", eventCount).ToArray()
        });
        cmd.Parameters.Add(new NpgsqlParameter("bdatas", NpgsqlDbType.Array | NpgsqlDbType.Bytea)
        {
            Value = new byte[]?[eventCount]
        });

        return cmd;
    }

    private async Task<(int StreamVersion, int MaxEventVersion)> readVersionsAsync(Guid streamId)
    {
        var schema = theStore.Options.Events.DatabaseSchemaName;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"select (select version from {schema}.mt_streams where id = @s)::int, (select max(version) from {schema}.mt_events where stream_id = @s)::int",
            conn);
        cmd.Parameters.AddWithValue("s", streamId);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task<bool> waitUntilBlockedOrDoneAsync(int backendPid, Task pending)
    {
        for (var i = 0; i < 200; i++)
        {
            if (pending.IsCompleted)
            {
                return true;
            }

            await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "select wait_event_type from pg_stat_activity where pid = @p", conn);
            cmd.Parameters.AddWithValue("p", backendPid);

            if (await cmd.ExecuteScalarAsync() as string == "Lock")
            {
                return true;
            }

            await Task.Delay(50);
        }

        return pending.IsCompleted;
    }
}
