using System;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests.Exceptions;

public class MartenCommandExceptionTests
{
    [Fact]
    public void should_create_MartenCommandException_when_command_is_null()
    {
        var createWithNullCommand = () => new MartenCommandException(null, new Exception());

        createWithNullCommand.ShouldNotThrow();
    }

    [Fact]
    public void command_text_is_recovered_from_a_recorded_batch_when_there_is_no_command()
    {
        var batch = new NpgsqlBatch();
        batch.BatchCommands.Add(new NpgsqlBatchCommand("select 1"));
        batch.BatchCommands.Add(new NpgsqlBatchCommand("delete from public.mt_doc_target where "));

        var inner = new Exception("boom");
        inner.RecordNpgsqlBatch(batch);

        var ex = new MartenCommandException(null, inner);

        ex.CommandText.ShouldContain("delete from public.mt_doc_target where");
        ex.Message.ShouldContain("delete from public.mt_doc_target where");
    }

    [Fact]
    public void a_big_batch_is_truncated_rather_than_dumped_in_full()
    {
        var batch = new NpgsqlBatch();
        for (var i = 0; i < 25; i++)
        {
            batch.BatchCommands.Add(new NpgsqlBatchCommand($"select {i}"));
        }

        var inner = new Exception("boom");
        inner.RecordNpgsqlBatch(batch);

        var ex = new MartenCommandException(null, inner);

        ex.CommandText.ShouldContain("select 0");
        ex.CommandText.ShouldNotContain("select 24");
        ex.CommandText.ShouldContain("and 20 more statement(s) in this batch");
    }
}

/// <summary>
///     A batched write that fails on malformed SQL has to report the offending statement.
///     Before this, ExecuteBatchPagesAsync threw a MartenCommandException whose message rendered an
///     empty CommandText, so the SQL was unrecoverable from the exception and from logs.
/// </summary>
public class batched_command_failures_report_their_sql: OneOffConfigurationsContext
{
    [Fact]
    public async Task malformed_sql_in_a_batch_reports_the_failing_statement()
    {

        // The exact shape reported in the field: a delete whose where clause rendered to nothing,
        // which PostgreSQL rejects with 42601 "syntax error at end of input".
        var badSql = $"delete from {SchemaName}.mt_doc_target where ";

        await using var session = theStore.LightweightSession();
        session.Store(Target.Random());
        session.QueueSqlCommand(badSql);

        var ex = await Should.ThrowAsync<MartenCommandException>(async () =>
            await session.SaveChangesAsync());

        ex.InnerException.ShouldBeOfType<PostgresException>()
            .SqlState.ShouldBe(PostgresErrorCodes.SyntaxError);

        ex.CommandText.ShouldNotBeNull();
        ex.CommandText.ShouldContain("mt_doc_target");
        ex.CommandText.Trim().ShouldEndWith("where");

        // and the same SQL has to be in the message, which is all most users ever see
        ex.Message.ShouldContain($"delete from {SchemaName}.mt_doc_target where");
    }
}
