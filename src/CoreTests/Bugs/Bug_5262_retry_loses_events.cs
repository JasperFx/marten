using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Exceptions;
using Marten.Services;
using Marten.Testing.Harness;
using Marten.Util;
using Npgsql;
using Polly;
using Shouldly;
using Xunit;

namespace CoreTests.Bugs;

/// <summary>
/// The other half of #5262: when the resilience pipeline's FIRST attempt times out and a LATER attempt
/// succeeds, does the retried batch still contain its events?
///
/// The first attempt is made to blow the command timeout and every attempt after it is fast, using a
/// sequence so the counter survives the rollback (nextval is not transactional).
/// </summary>
public class Bug_5262_retry_loses_events: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public Bug_5262_retry_loses_events(ITestOutputHelper output)
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
    public async Task a_successful_retry_still_writes_its_events()
    {
        StoreOptions(opts =>
        {
            opts.CommandTimeout = 1;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;

            // The permissive default, explicitly, so this test measures the retry itself and not the
            // narrowed write policy from this branch.
            opts.ConfigurePolly(builder => builder.AddMartenDefaults());
        });

        var streamKeys = Enumerable.Range(0, Count).Select(i => $"stream-{i}").ToArray();

        await using (var setup = theStore.LightweightSession())
        {
            foreach (var key in streamKeys)
            {
                setup.Events.StartStream(key, new StreamStarted(key));
            }

            await setup.SaveChangesAsync();
        }

        // After the setup commit, so the schema exists.
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"drop sequence if exists {SchemaName}.attempt_counter; create sequence {SchemaName}.attempt_counter;";
            await cmd.ExecuteNonQueryAsync();
        }

        var logger = new CountingLogger();

        await using var session = theStore.LightweightSession();
        session.Logger = logger;

        for (var i = 0; i < Count; i++)
        {
            session.Events.Append(streamKeys[i], new SomethingHappened($"event-{i}"));
            session.Store(new Doc { Id = Guid.NewGuid(), Name = $"doc-{i}" });
        }

        // Sleeps 3 seconds on the first attempt only. nextval survives the rollback.
        session.QueueSqlCommand(
            $"select pg_sleep(case when nextval('{SchemaName}.attempt_counter') <= 1 then 3 else 0 end)");

        Exception caught = null;
        try
        {
            await session.SaveChangesAsync();
        }
        catch (Exception e)
        {
            caught = e;
        }

        var (events, docs, tombstones, attempts) = await MeasureAsync();

        _output.WriteLine($"exception from SaveChangesAsync : {caught?.GetType().Name ?? "<none>"}");
        _output.WriteLine($"batches executed                : {logger.BatchesStarted}");
        _output.WriteLine($"attempt counter (nextval)       : {attempts}");
        _output.WriteLine($"persisted                       : {events} events, {docs} documents, {tombstones} tombstones");
        _output.WriteLine($"expected                        : {Count} events, {Count} documents");

        // A retry that commits must commit the whole unit of work, events included.
        caught.ShouldBeNull();
        docs.ShouldBe(Count);
        events.ShouldBe(Count);
    }

    private async Task<(int, int, int, long)> MeasureAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        async Task<long> ScalarAsync(string sql)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        var events = (int)await ScalarAsync($"select count(*) from {SchemaName}.mt_events where type = 'something_happened'");
        var docs = (int)await ScalarAsync($"select count(*) from {SchemaName}.mt_doc_bug_5262_retry_loses_events_doc");
        var tombstones = (int)await ScalarAsync($"select count(*) from {SchemaName}.mt_events where type = 'tombstone'");
        var attempts = await ScalarAsync($"select last_value from {SchemaName}.attempt_counter");

        return (events, docs, tombstones, attempts);
    }

    public class CountingLogger: IMartenSessionLogger
    {
        public int BatchesStarted { get; private set; }

        public void LogSuccess(NpgsqlCommand command) { }
        public void LogSuccess(NpgsqlBatch batch) { }
        public void LogFailure(NpgsqlCommand command, Exception ex) { }
        public void LogFailure(NpgsqlBatch batch, Exception ex) { }
        public void LogFailure(Exception ex, string message) { }
        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit) { }
        public void OnBeforeExecute(NpgsqlCommand command) { }
        public void OnBeforeExecute(NpgsqlBatch batch) => BatchesStarted++;
    }
}
