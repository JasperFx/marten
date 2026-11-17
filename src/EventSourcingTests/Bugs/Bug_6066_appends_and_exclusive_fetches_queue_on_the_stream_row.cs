#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #6066. <c>mt_quick_append_events</c> inserted a stream's events, whose foreign key takes a shared lock
/// on the <c>mt_streams</c> row, before it updated that row. Two appenders to one stream could each hold
/// the shared lock and wait on the other, and a <c>FetchForExclusiveWriting</c> waiting for the row joined
/// the cycle, so PostgreSQL failed one of them with a deadlock (40P01). The function now locks the row when
/// it reads the version, before inserting anything.
/// </summary>
public class Bug_6066_appends_and_exclusive_fetches_queue_on_the_stream_row: OneOffConfigurationsContext
{
    private const int Writers = 6;
    private const int AppendsPerWriter = 12;

    public enum Write
    {
        Append,
        FetchForWriting,
        FetchForExclusiveWriting,
        AppendOptimistic
    }

    [Theory]
    [InlineData(EventAppendMode.Quick, false)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps, false)]
    [InlineData(EventAppendMode.Quick, true)]
    [InlineData(EventAppendMode.Rich, false)]
    public async Task writers_on_one_stream_never_deadlock(EventAppendMode mode, bool exclusiveLockOnConcurrentAppends)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = mode;
            opts.Events.UseExclusiveLockOnConcurrentAppends = exclusiveLockOnConcurrentAppends;
        });

        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<WriteTally>(id, new Written(-1));
            await session.SaveChangesAsync(ct);
        }

        var failures = new ConcurrentQueue<string>();
        var conflicts = 0;

        async Task writeAsync(int writer)
        {
            var done = 0;
            for (var attempt = 0; done < AppendsPerWriter && attempt < AppendsPerWriter * 50; attempt++)
            {
                var kind = (Write)((writer + attempt) % Enum.GetValues<Write>().Length);
                try
                {
                    await using var session = theStore.LightweightSession();
                    switch (kind)
                    {
                        case Write.Append:
                            session.Events.Append(id, new Written(writer));
                            break;

                        case Write.FetchForWriting:
                            (await session.Events.FetchForWriting<WriteTally>(id, ct)).AppendOne(new Written(writer));
                            break;

                        case Write.FetchForExclusiveWriting:
                            (await session.Events.FetchForExclusiveWriting<WriteTally>(id, ct)).AppendOne(new Written(writer));
                            break;

                        case Write.AppendOptimistic:
                            await session.Events.AppendOptimistic(id, ct, new Written(writer));
                            break;
                    }

                    await session.SaveChangesAsync(ct);
                    done++;
                }
                catch (Exception e) when (e is ConcurrencyException or EventStreamUnexpectedMaxEventIdException)
                {
                    // Another writer appended first; fetch again
                    Interlocked.Increment(ref conflicts);
                }
                catch (Exception e)
                {
                    failures.Enqueue($"{kind} by writer {writer}: {e}");
                    return;
                }
            }

            if (done < AppendsPerWriter) failures.Enqueue($"Writer {writer} only appended {done} times");
        }

        await Task.WhenAll(Enumerable.Range(0, Writers).Select(w => Task.Run(() => writeAsync(w), ct)));

        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures));

        // Every append is in the stream exactly once, at consecutive versions
        await using var query = theStore.LightweightSession();
        var events = await query.Events.FetchStreamAsync(id, token: ct);
        events.Select(x => x.Version).ShouldBe(Enumerable.Range(1, 1 + Writers * AppendsPerWriter).Select(x => (long)x));

        var state = await query.Events.FetchStreamStateAsync(id, ct);
        state!.Version.ShouldBe(1 + Writers * AppendsPerWriter);

        var stream = await query.Events.FetchForWriting<WriteTally>(id, ct);
        stream.Aggregate!.Seen.ShouldBe(Enumerable.Range(1, 1 + Writers * AppendsPerWriter).Select(x => (long)x).ToList());
    }

    [Fact]
    public async Task an_existing_database_gets_the_function_that_locks_the_stream_row_first()
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.AddEventType(typeof(Written));
        });
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Put back the function as it was before #6066, with no lock on the version read
        var current = await functionDefinitionAsync();
        current.ShouldContain("for no key update");
        await executeAsync(current.Replace(" for no key update;", ";"));
        (await functionDefinitionAsync()).ShouldNotContain("for no key update");

        await Should.ThrowAsync<Exception>(() => theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync());

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await functionDefinitionAsync()).ShouldContain("for no key update");
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    private async Task<string> functionDefinitionAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select pg_get_functiondef(p.oid) from pg_proc p join pg_namespace n on n.oid = p.pronamespace " +
                          "where p.proname = 'mt_quick_append_events' and n.nspname = @schema";
        cmd.Parameters.AddWithValue("schema", SchemaName);
        return (string)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task executeAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}

public record Written(int Writer);

public class WriteTally
{
    public Guid Id { get; set; }
    public List<long> Seen { get; set; } = new();

    public void Apply(IEvent<Written> e) => Seen.Add(e.Version);
}
