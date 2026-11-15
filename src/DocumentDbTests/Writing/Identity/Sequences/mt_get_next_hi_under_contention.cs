using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity.Sequences;

/// <summary>
///     #5938: <c>mt_get_next_hi</c> used to read the hi and then compare-and-set it, returning -1 to any
///     caller whose read was overtaken by another caller's commit. Every overlap cost the loser one of its
///     <c>MaxAdvanceToNextHiAttempts</c>, with no backoff and no fairness, so under concurrent load a
///     sequence could lose all thirty and throw <c>HiloSequenceAdvanceToNextHiAttemptsExceededException</c>.
///     It is now a single atomic increment: overlapping callers queue on the row lock and each gets a hi.
/// </summary>
public class mt_get_next_hi_under_contention: OneOffConfigurationsContext
{
    private string schema => theStore.Options.DatabaseSchemaName;

    private async Task<NpgsqlConnection> openAsync()
    {
        // Reading Sequences applies mt_hilo and mt_get_next_hi
        theStore.Tenancy.Default.Database.Sequences.ShouldNotBeNull();

        var conn = theStore.Tenancy.Default.Database.CreateConnection();
        await conn.OpenAsync();
        return conn;
    }

    private async Task<long> nextHi(NpgsqlConnection conn, string entity, NpgsqlTransaction tx = null)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"select {schema}.mt_get_next_hi(@entity)";
        cmd.Parameters.AddWithValue("entity", entity);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task waitUntilBlockedOnALock(NpgsqlConnection observer, int pid)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var cmd = observer.CreateCommand();
            cmd.CommandText = "select wait_event_type from pg_stat_activity where pid = @pid";
            cmd.Parameters.AddWithValue("pid", pid);
            if (await cmd.ExecuteScalarAsync() is "Lock") return;

            await Task.Delay(10);
        }

        throw new TimeoutException($"Backend {pid} never blocked on a lock");
    }

    private static string uniqueEntity() => "contention_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task a_call_blocked_behind_another_gets_the_next_hi_instead_of_failing()
    {
        var entity = uniqueEntity();

        await using var holder = await openAsync();
        await using var waiter = await openAsync();
        await using var observer = await openAsync();

        (await nextHi(holder, entity)).ShouldBe(0);

        // The holder has incremented the row and not committed, as a fetch has during its round trip
        await using var tx = await holder.BeginTransactionAsync();
        (await nextHi(holder, entity, tx)).ShouldBe(1);

        var blocked = nextHi(waiter, entity);
        await waitUntilBlockedOnALock(observer, waiter.ProcessID);

        await tx.CommitAsync();

        // The old function read 0, found 1 committed under it and returned -1
        (await blocked).ShouldBe(2);
    }

    [Fact]
    public async Task concurrent_first_calls_for_a_new_entity_both_get_a_hi()
    {
        var entity = uniqueEntity();

        await using var holder = await openAsync();
        await using var waiter = await openAsync();
        await using var observer = await openAsync();

        // The holder inserts the entity's row and has not committed it
        await using var tx = await holder.BeginTransactionAsync();
        (await nextHi(holder, entity, tx)).ShouldBe(0);

        var blocked = nextHi(waiter, entity);
        await waitUntilBlockedOnALock(observer, waiter.ProcessID);

        await tx.CommitAsync();

        // The old function inserted a second row for the entity and failed with a unique violation
        (await blocked).ShouldBe(1);
    }

    [Fact]
    public async Task every_concurrent_caller_gets_a_distinct_hi_without_a_retry()
    {
        var entity = uniqueEntity();

        const int callers = 16;
        const int callsPerCaller = 50;

        // Build the store and apply the hilo objects once, before the callers race to
        await using (await openAsync())
        {
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, callers).Select(_ => Task.Run(async () =>
        {
            await using var conn = await openAsync();
            await start.Task;

            var his = new long[callsPerCaller];
            for (var i = 0; i < callsPerCaller; i++)
            {
                his[i] = await nextHi(conn, entity);
            }

            return his;
        })).ToArray();

        start.SetResult();
        var all = (await Task.WhenAll(tasks)).SelectMany(x => x).OrderBy(x => x).ToArray();

        // No call came back with -1, and together they used every hi exactly once
        all.ShouldBe(Enumerable.Range(0, callers * callsPerCaller).Select(x => (long)x).ToArray());
    }
}
