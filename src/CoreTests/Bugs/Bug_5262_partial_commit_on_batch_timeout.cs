using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests.Bugs;

/// <summary>
/// Repro for #5262. A SaveChangesAsync that mixes event appends with document writes and blows the
/// command timeout
///
/// 1. is retried in full by <c>Options.ResiliencePipeline</c> (DocumentSessionBase.SaveChanges.cs:170) -
///    including the event appends, which are not idempotent, and
/// 2. leaves state behind - mt_streams.version is bumped for streams whose events were never written.
///
/// (2) is the exact fingerprint we see in production: streams whose <c>version</c> runs ahead of their
/// own event count, on the worst one by 216.
/// </summary>
public class Bug_5262_partial_commit_on_batch_timeout: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public Bug_5262_partial_commit_on_batch_timeout(ITestOutputHelper output)
    {
        _output = output;
    }

    public class Doc
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }

    public record StreamStarted(string Name);

    public record SomethingHappened(string Name);

    private const int Count = 30;

    [Fact]
    public async Task batch_timeout_rolls_everything_back_and_runs_the_batch_once()
    {
        StoreOptions(opts =>
        {
            opts.CommandTimeout = 1; // seconds - the Marten default is 5
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps; // the Marten 9 default
        });

        var streamKeys = Enumerable.Range(0, Count).Select(i => $"stream-{i}").ToArray();

        // Arrange: the streams already exist, exactly like the production case. Appends to an existing
        // stream go through mt_quick_append_events.
        await using (var setup = theStore.LightweightSession())
        {
            foreach (var key in streamKeys)
            {
                setup.Events.StartStream(key, new StreamStarted(key));
            }

            await setup.SaveChangesAsync();
        }

        var logger = new RecordingLogger();

        await using var session = theStore.LightweightSession();
        session.Logger = logger;

        for (var i = 0; i < Count; i++)
        {
            session.Events.Append(streamKeys[i], new SomethingHappened($"event-{i}"));
            session.Store(new Doc { Id = Guid.NewGuid(), Name = $"doc-{i}" });
        }

        // Pushes the batch past the 1 second command timeout. Event operations go first in the batch
        // (UnitOfWork.AllOperations), so the appends have already been sent when this one stalls.
        session.QueueSqlCommand("select pg_sleep(3)");

        Exception caught = null;
        try
        {
            await session.SaveChangesAsync();
        }
        catch (Exception e)
        {
            caught = e;
        }

        _output.WriteLine($"exception  : {caught?.GetType().FullName ?? "<none>"} / inner {caught?.InnerException?.GetType().Name}");
        _output.WriteLine($"batches executed (OnBeforeExecute) : {logger.BatchesStarted}");
        _output.WriteLine($"batch commands logged as failed    : {logger.FailedCommands.Count}");
        foreach (var group in logger.FailedCommands.GroupBy(NormalizeSql).OrderByDescending(g => g.Count()))
        {
            _output.WriteLine($"  {group.Count(),4} x {group.Key}");
        }

        // Measure on a fresh connection, bypassing Marten entirely.
        var (rows, extraEvents, docs, tombstones) = await MeasureAsync();

        _output.WriteLine($"after the failure: {extraEvents} SomethingHappened events, {docs} documents, {tombstones} tombstones");
        _output.WriteLine("streams whose version does not match their own event count:");
        var mismatched = rows.Where(x => x.Id != "mt_tombstone" && x.Version != x.Events).ToList();
        foreach (var row in mismatched)
        {
            _output.WriteLine($"  {row.Id}: mt_streams.version = {row.Version}, actual events = {row.Events}");
        }

        // Marten's contract: one transaction, so all of it or none of it. And the batch belongs to the
        // caller to retry - re-appending events behind their back is not safe.
        caught.ShouldNotBeNull("SaveChangesAsync returned normally after the batch failed");
        logger.BatchesStarted.ShouldBe(1);

        extraEvents.ShouldBe(0);
        docs.ShouldBe(0);
        mismatched.ShouldBeEmpty();
    }

    private async Task<(List<StreamRow>, int, int, int)> MeasureAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var rows = new List<StreamRow>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                               select s.id, s.version, (select count(*) from {SchemaName}.mt_events e where e.stream_id = s.id)
                               from {SchemaName}.mt_streams s order by s.id
                               """;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new StreamRow
                {
                    Id = reader.GetString(0), Version = reader.GetInt64(1), Events = reader.GetInt64(2)
                });
            }
        }

        async Task<int> ScalarAsync(string sql)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        var extraEvents = await ScalarAsync($"select count(*) from {SchemaName}.mt_events where type = 'something_happened'");
        var docs = await ScalarAsync($"select count(*) from {SchemaName}.mt_doc_bug_5262_partial_commit_on_batch_timeout_doc");
        var tombstones = await ScalarAsync($"select count(*) from {SchemaName}.mt_events where type = 'tombstone'");

        return (rows, extraEvents, docs, tombstones);
    }

    public class StreamRow
    {
        public string Id { get; set; }
        public long Version { get; set; }
        public long Events { get; set; }
    }

    private static string NormalizeSql(string sql)
    {
        var trimmed = sql.Trim().ReplaceLineEndings(" ");
        return trimmed.Length <= 70 ? trimmed : trimmed[..70];
    }

    public class RecordingLogger: IMartenSessionLogger
    {
        public List<string> FailedCommands { get; } = new();
        public int BatchesStarted { get; private set; }

        public void LogSuccess(NpgsqlCommand command) { }
        public void LogSuccess(NpgsqlBatch batch) { }

        public void LogFailure(NpgsqlCommand command, Exception ex) => FailedCommands.Add(command.CommandText);

        public void LogFailure(NpgsqlBatch batch, Exception ex)
        {
            foreach (var command in batch.BatchCommands)
            {
                FailedCommands.Add(command.CommandText);
            }
        }

        public void LogFailure(Exception ex, string message) { }
        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit) { }
        public void OnBeforeExecute(NpgsqlCommand command) { }
        public void OnBeforeExecute(NpgsqlBatch batch) => BatchesStarted++;
    }
}
