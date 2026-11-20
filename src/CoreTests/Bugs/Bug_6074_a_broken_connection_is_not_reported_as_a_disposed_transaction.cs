#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests.Bugs;

public class Bug6074Doc
{
    public Guid Id { get; set; }
    public int Value { get; set; }
}

/// <summary>
/// #6074. When the connection breaks inside a write transaction, Npgsql disposes the transaction along with the
/// connector. Rolling it back then throws <see cref="ObjectDisposedException" /> for <c>NpgsqlTransaction</c>, and
/// the unit of work and the bulk insert threw that instead of the failure that broke the connection. Under a
/// heavily loaded gate that failure is a command timeout, which is how the disposed transaction showed up in
/// tests that have nothing to do with transactions.
/// </summary>
public class Bug_6074_a_broken_connection_is_not_reported_as_a_disposed_transaction: OneOffConfigurationsContext
{
    [Fact]
    public async Task a_unit_of_work_whose_connection_breaks_reports_the_break()
    {
        await using var session = theStore.LightweightSession();
        session.Store(new Bug6074Doc { Id = Guid.NewGuid(), Value = 1 });

        // The backend ends itself inside the unit of work's transaction
        session.QueueSqlCommand("select pg_terminate_backend(pg_backend_pid())");

        var thrown = await Should.ThrowAsync<Exception>(() => session.SaveChangesAsync());

        shouldBeTheBreak(thrown);
    }

    [Fact]
    public async Task a_bulk_insert_whose_connection_breaks_reports_the_break()
    {
        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(Bug6074Doc));
        var table = theStore.Options.Storage.MappingFor(typeof(Bug6074Doc)).TableName.QualifiedName;

        // Hold a lock that keeps the bulk insert's COPY waiting inside its transaction
        await using var holder = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await holder.OpenAsync();
        await using var holding = await holder.BeginTransactionAsync();
        await using (var take = new NpgsqlCommand($"lock table {table} in share mode", holder, holding))
        {
            await take.ExecuteNonQueryAsync();
        }

        var docs = new[] { new Bug6074Doc { Id = Guid.NewGuid(), Value = 1 } };
        var bulkInsert = theStore.BulkInsertDocumentsAsync(docs);

        // End the bulk insert's backend once it is waiting behind the lock
        var blocked = await theBackendBlockedByAsync(holder.ProcessID);
        await using (var killer = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await killer.OpenAsync();
            await using var kill = new NpgsqlCommand("select pg_terminate_backend(:pid)", killer);
            kill.Parameters.AddWithValue("pid", blocked);
            await kill.ExecuteScalarAsync();
        }

        var thrown = await Should.ThrowAsync<Exception>(() => bulkInsert);

        shouldBeTheBreak(thrown);
    }

    private static async Task<int> theBackendBlockedByAsync(int holder)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select pid from pg_stat_activity where :holder = any(pg_blocking_pids(pid)) limit 1", conn);
        cmd.Parameters.AddWithValue("holder", holder);

        var giveUp = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            if (await cmd.ExecuteScalarAsync() is int pid) return pid;

            if (DateTime.UtcNow > giveUp) throw new TimeoutException("The bulk insert never waited on the lock");
            await Task.Delay(10);
        }
    }

    private static void shouldBeTheBreak(Exception thrown)
    {
        for (var e = thrown; e != null; e = e.InnerException)
        {
            e.ShouldNotBeOfType<ObjectDisposedException>(
                "the rollback of the transaction Npgsql disposed with the broken connection replaced the failure");
        }

        PostgresException? terminated = null;
        for (var e = (Exception?)thrown; e != null; e = e.InnerException)
        {
            if (e is PostgresException pg)
            {
                terminated = pg;
                break;
            }
        }

        terminated.ShouldNotBeNull($"expected the termination as the cause, but got {thrown}");
        terminated.SqlState.ShouldBe(PostgresErrorCodes.AdminShutdown);
    }
}
