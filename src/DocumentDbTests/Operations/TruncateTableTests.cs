using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Marten;
using Marten.Internal.Operations;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Operations;

public class TruncateTableTests : IntegrationContext
{
    public TruncateTableTests(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task truncate_by_document()
    {
        var targets = Target.GenerateRandomData(100).ToList();
        await theStore.BulkInsertAsync(targets);

        var op = new TruncateTable(typeof(Target));
        theSession.QueueOperation(op);
        await saveChangesReportingLockHoldersAsync();

        var count = await theSession.Query<Target>().CountAsync();
        count.ShouldBe(0);
    }

    [Fact]
    public async Task truncate_by_table_name()
    {
        var targets = Target.GenerateRandomData(100).ToList();
        await theStore.BulkInsertAsync(targets);

        var tableName = theStore.Options.Storage.MappingFor(typeof(Target)).TableName;
        var op = new TruncateTable(tableName);
        theSession.QueueOperation(op);
        await saveChangesReportingLockHoldersAsync();

        var count = await theSession.Query<Target>().CountAsync();
        count.ShouldBe(0);
    }

    /// <summary>
    ///     #5935: in CI the TRUNCATE intermittently waits past the command timeout on a lock that something else
    ///     holds on the Target table, and the failure says nothing about who. Rethrow with the lock holders and
    ///     every connection idle in a transaction, so the next occurrence names the culprit. Remove once #5935 is
    ///     understood and fixed.
    /// </summary>
    private async Task saveChangesReportingLockHoldersAsync()
    {
        try
        {
            await theSession.SaveChangesAsync();
        }
        catch (Exception e)
        {
            string report;
            try
            {
                report = await describeLockHoldersAsync();
            }
            catch (Exception reportFailure)
            {
                report = $"(could not read pg_locks: {reportFailure.Message})";
            }

            throw new Exception($"#5935: TRUNCATE of the Target table failed. Lock holders at the time:{Environment.NewLine}{report}", e);
        }
    }

    private async Task<string> describeLockHoldersAsync()
    {
        var table = theStore.Options.Storage.MappingFor(typeof(Target)).TableName.QualifiedName;

        // A fresh connection, not the session's: the session's may already be broken by the timeout
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var report = new StringBuilder();

        await using (var locks = conn.CreateCommand())
        {
            locks.CommandText = @"
select a.pid, a.state, a.application_name, a.backend_xid::text, a.xact_start::text, a.state_change::text,
       l.mode, l.granted, left(coalesce(a.query, ''), 300)
from pg_locks l join pg_stat_activity a on a.pid = l.pid
where l.relation = to_regclass(@table)
order by l.granted desc, a.xact_start";
            locks.Parameters.AddWithValue("table", table);
            report.AppendLine($"Locks on {table} (pid | state | application | xid | xact_start | state_change | mode | granted | query):");
            await appendRowsAsync(locks, report);
        }

        await using (var idle = conn.CreateCommand())
        {
            idle.CommandText = @"
select a.pid, a.state, a.application_name, a.backend_xid::text, a.xact_start::text, a.state_change::text,
       left(coalesce(a.query, ''), 300)
from pg_stat_activity a
where a.datname = current_database() and a.pid <> pg_backend_pid() and a.state like 'idle in transaction%'
order by a.xact_start";
            report.AppendLine("Connections idle in a transaction (pid | state | application | xid | xact_start | state_change | last query):");
            await appendRowsAsync(idle, report);
        }

        return report.ToString();
    }

    private static async Task appendRowsAsync(NpgsqlCommand command, StringBuilder report)
    {
        await using var reader = await command.ExecuteReaderAsync();
        var any = false;
        while (await reader.ReadAsync())
        {
            any = true;
            report.Append("  ");
            for (var i = 0; i < reader.FieldCount; i++)
            {
                report.Append(reader.IsDBNull(i) ? "null" : reader.GetValue(i)).Append(" | ");
            }

            report.AppendLine();
        }

        if (!any)
        {
            report.AppendLine("  (none)");
        }
    }
}
