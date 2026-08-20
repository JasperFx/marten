using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
///     #5258. The value proposition of the aggregate write cache under the <b>Inline</b> lifecycle is entirely
///     the removed snapshot load — unlike Async there is no delta query to shrink, so if the commit turns around
///     and reloads the document, the feature buys nothing but a <c>TryTake</c> and a cache write.
///     <para>
///     Every other test in this area infers that. These count, because the claim has two halves and only the
///     first is visible in the code: <c>FetchInlinedPlan.fetchForWriting</c> plainly does not add the
///     <c>LoadByIdHandler</c> command to the batch on a hit, but nothing anywhere proved that the inline
///     projection does not load the same snapshot again during <c>SaveChangesAsync</c>.
///     </para>
/// </summary>
public class inline_cache_hit_query_counts: OneOffConfigurationsContext
{
    private readonly MartenTestAggregateWriteCache theCache = new();

    private void ConfigureStore(bool cached)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);

            if (cached)
            {
                opts.Events.AggregateWriteCaching.Cache = theCache;
                opts.Events.CacheAggregatesForWriting<SimpleAggregate>();
            }
        });
    }

    /// <summary>
    ///     Runs one fetch-append-commit round against an already-seeded stream and returns every statement the
    ///     session issued, so a test can count reads of the aggregate's own table across the WHOLE round rather
    ///     than just the fetch half.
    /// </summary>
    private async Task<(SimpleAggregate Aggregate, IReadOnlyList<string> Sql)> RoundTripAsync(bool cached)
    {
        ConfigureStore(cached);

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        // A first fetch-append-commit is what seeds the cache: the entry is written back after the commit,
        // never at fetch time.
        await using (var warmUp = theStore.LightweightSession())
        {
            var warmUpStream = await warmUp.Events.FetchForWriting<SimpleAggregate>(streamId);
            warmUpStream.AppendOne(new CEvent());
            await warmUp.SaveChangesAsync();
        }

        var logger = new SqlCollector();
        await using var session = theStore.LightweightSession();
        session.Logger = logger;

        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.AppendOne(new CEvent());
        await session.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        return ((await query.LoadAsync<SimpleAggregate>(streamId))!, logger.Sql);
    }

    private static int ReadsOfTheAggregateTable(IReadOnlyList<string> sql) =>
        sql.Count(x => x.TrimStart().StartsWith("select", StringComparison.OrdinalIgnoreCase)
                       && x.Contains("mt_doc_simpleaggregate", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task an_uncached_round_reads_the_snapshot_exactly_once()
    {
        var (aggregate, sql) = await RoundTripAsync(cached: false);

        // The baseline the cached number has to be read against: one load, on the fetch side. The commit does
        // not load it a second time because the fetch put the instance in the session's item map.
        ReadsOfTheAggregateTable(sql).ShouldBe(1);

        aggregate.ACount.ShouldBe(1);
        aggregate.CCount.ShouldBe(2);
        aggregate.Version.ShouldBe(4);
    }

    [Fact]
    public async Task a_cache_hit_never_reads_the_snapshot_at_all()
    {
        var (aggregate, sql) = await RoundTripAsync(cached: true);

        theCache.Hits.ShouldBe(1);

        // The whole point. Not "one fewer than the fetch would have done" -- zero, across the entire round,
        // which is only reachable if the commit-side load is skipped too.
        ReadsOfTheAggregateTable(sql).ShouldBe(0);

        // And the result is still right, so this is not a win by doing nothing.
        aggregate.ACount.ShouldBe(1);
        aggregate.CCount.ShouldBe(2);
        aggregate.Version.ShouldBe(4);
    }

    [Fact]
    public async Task the_stream_still_sees_the_events_it_appended()
    {
        // Guards the obvious way for the assertion above to be satisfied wrongly: a round that quietly stopped
        // doing the work would also read the table zero times.
        ConfigureStore(cached: true);

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent());
        await theSession.SaveChangesAsync();

        await using (var warmUp = theStore.LightweightSession())
        {
            var warmUpStream = await warmUp.Events.FetchForWriting<SimpleAggregate>(streamId);
            warmUpStream.AppendOne(new BEvent());
            await warmUp.SaveChangesAsync();
        }

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        theCache.Hits.ShouldBe(1);
        stream.Aggregate.ShouldNotBeNull();
        stream.CurrentVersion.ShouldBe(2);

        stream.AppendOne(new CEvent());
        await session.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        var reloaded = await query.LoadAsync<SimpleAggregate>(streamId);
        reloaded!.Version.ShouldBe(3);
        reloaded.CCount.ShouldBe(1);
    }

    /// <summary>
    ///     Captures single commands AND the contents of every batch — the commit side runs as an
    ///     <see cref="NpgsqlBatch" />, so a collector that only handled <see cref="NpgsqlCommand" /> would miss
    ///     exactly the half this is trying to measure.
    /// </summary>
    public class SqlCollector: IMartenSessionLogger
    {
        public List<string> Sql { get; } = new();

        public void LogSuccess(NpgsqlCommand command) => Sql.Add(command.CommandText);
        public void LogFailure(NpgsqlCommand command, Exception ex) => Sql.Add(command.CommandText);

        public void LogSuccess(NpgsqlBatch batch)
        {
            foreach (var command in batch.BatchCommands) Sql.Add(command.CommandText);
        }

        public void LogFailure(NpgsqlBatch batch, Exception ex)
        {
            foreach (var command in batch.BatchCommands) Sql.Add(command.CommandText);
        }

        public void LogFailure(Exception ex, string message) { }
        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit) { }
        public void OnBeforeExecute(NpgsqlCommand command) { }
        public void OnBeforeExecute(NpgsqlBatch batch) { }
    }
}
