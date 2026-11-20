#pragma warning disable MARTEN001 // cold event storage is experimental
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.FetchForWriting;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Cold;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Cold.Matrix;

/// <summary>
///     One store per combination of the store-wide settings the matrix varies: the lifecycles it holds,
///     <c>UseIdentityMapForAggregates</c>, and conjoined tenancy. Every aggregate type of those lifecycles is
///     registered, once with the aggregate write cache and once without, by stream key and by natural key.
/// </summary>
public abstract class ColdMatrixFixture: IAsyncLifetime
{
    public static readonly TimeSpan Generous = 60.Seconds();

    protected ColdMatrixFixture(string schemaName, bool async, bool identityMapForAggregates, bool conjoined)
    {
        SchemaName = schemaName;
        Async = async;
        IdentityMapForAggregates = identityMapForAggregates;
        Conjoined = conjoined;
    }

    public string SchemaName { get; }
    public bool Async { get; }
    public bool IdentityMapForAggregates { get; }
    public bool Conjoined { get; }

    public string TenantId => Conjoined ? "cold-red" : StorageConstants.DefaultTenantId;

    public InMemoryColdEventStore Cold { get; } = new();
    public MartenTestAggregateWriteCache Cache { get; } = new();
    public DocumentStore Store { get; private set; } = null!;
    private IProjectionDaemon? _daemon;

    public async ValueTask InitializeAsync()
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.CreateCommand($"drop schema if exists {SchemaName} cascade").ExecuteNonQueryAsync();
        }

        var opts = new StoreOptions();
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = SchemaName;
        opts.AutoCreateSchemaObjects = AutoCreate.All;
        opts.NameDataLength = 100;
        opts.DisableNpgsqlLogging = true;

        opts.Events.StreamIdentity = StreamIdentity.AsString;
        opts.Events.UseColdEventStore(Cold);
        opts.Events.UseIdentityMapForAggregates = IdentityMapForAggregates;
        opts.Events.AggregateWriteCaching.Cache = Cache;

        if (Conjoined)
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
        }

        if (Async)
        {
            opts.Projections.Snapshot<AsyncKeyedCart>(SnapshotLifecycle.Async);
            opts.Projections.Snapshot<AsyncKeyedCachedCart>(SnapshotLifecycle.Async);
            opts.Projections.Snapshot<AsyncNumberedCart>(SnapshotLifecycle.Async);
            opts.Projections.Snapshot<AsyncNumberedCachedCart>(SnapshotLifecycle.Async);
            opts.Events.CacheAggregatesForWriting<AsyncKeyedCachedCart>();
            opts.Events.CacheAggregatesForWriting<AsyncNumberedCachedCart>();
        }
        else
        {
            opts.Projections.LiveStreamAggregation<LiveKeyedCart>();
            opts.Projections.LiveStreamAggregation<LiveKeyedCachedCart>();
            opts.Projections.LiveStreamAggregation<LiveNumberedCart>();
            opts.Projections.LiveStreamAggregation<LiveNumberedCachedCart>();
            opts.Events.CacheAggregatesForWriting<LiveKeyedCachedCart>();
            opts.Events.CacheAggregatesForWriting<LiveNumberedCachedCart>();

            opts.Projections.Snapshot<InlineKeyedCart>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<InlineKeyedCachedCart>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<InlineNumberedCart>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<InlineNumberedCachedCart>(SnapshotLifecycle.Inline);
            opts.Events.CacheAggregatesForWriting<InlineKeyedCachedCart>();
            opts.Events.CacheAggregatesForWriting<InlineNumberedCachedCart>();
        }

        Store = new DocumentStore(opts);
        await Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        if (Async)
        {
            _daemon = await Store.BuildProjectionDaemonAsync();
            await _daemon.StartAllAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_daemon != null)
        {
            await _daemon.StopAllAsync();
            _daemon.Dispose();
        }

        Store?.Dispose();
    }

    /// <summary>Every async shard has processed every event, which <c>MarkStreamsCold</c> waits for.</summary>
    public Task CatchUpAsync() =>
        Async ? Store.Storage.Database.WaitForNonStaleProjectionDataAsync(Generous) : Task.CompletedTask;

    public IDocumentSession OpenSession(ColdSession session = ColdSession.Lightweight) =>
        Store.OpenSession(new SessionOptions
        {
            TenantId = TenantId,
            Tracking = session switch
            {
                ColdSession.IdentityMap => DocumentTracking.IdentityOnly,
                ColdSession.DirtyTracked => DocumentTracking.DirtyTracking,
                _ => DocumentTracking.None
            }
        });

    public Task MoveToColdAsync(string key, long throughVersion) =>
        Store.MoveToColdAsync(Cold, TenantId, key, throughVersion);
}

public class LiveAndInlineColdMatrixFixture(): ColdMatrixFixture("cold_matrix_live_inline", false, false, false);

public class LiveAndInlineWithIdentityMapColdMatrixFixture()
    : ColdMatrixFixture("cold_matrix_live_inline_idmap", false, true, false);

public class LiveAndInlineConjoinedColdMatrixFixture()
    : ColdMatrixFixture("cold_matrix_live_inline_conjoined", false, false, true);

public class AsyncColdMatrixFixture(): ColdMatrixFixture("cold_matrix_async", true, false, false);

public class AsyncWithIdentityMapColdMatrixFixture(): ColdMatrixFixture("cold_matrix_async_idmap", true, true, false);

/// <summary>
///     The permutations, and what each one does. Every case folds the same stream (opened by "anne", then items
///     a, b and c) after it went cold in one of the <see cref="ColdShape" />s, and expects the whole history back.
///     A writing fetch then appends item d and commits, and the stream is read again.
/// </summary>
public static class ColdMatrix
{
    /// <summary>
    ///     The identities a cold store accepts. Guid stream identity is refused at startup today; adding it here
    ///     (with Guid-keyed aggregate types) is all the matrix needs once it is supported.
    /// </summary>
    public static readonly ColdIdentity[] SupportedIdentities = [ColdIdentity.String, ColdIdentity.NaturalKey];

    public static readonly string[] Expected = ["a", "b", "c"];

    /// <summary>The fetches each identity has.</summary>
    public static IEnumerable<ColdFetch> FetchesFor(ColdIdentity identity) => identity switch
    {
        // Natural keys have no batched form, no expected-version form and no FetchManyForWriting, and
        // AggregateStreamAsync and ProjectLatest take a stream identity
        ColdIdentity.NaturalKey =>
            [ColdFetch.FetchForWriting, ColdFetch.FetchForExclusiveWriting, ColdFetch.FetchLatest],
        _ => Enum.GetValues<ColdFetch>()
    };

    /// <summary>
    ///     What an aggregate's stored document can have become. A document is only moved out with the last of the
    ///     stream's events: with warm events left after the cold part, a missing document is the projection's own
    ///     outcome (it was deleted), as <c>cold-storage.md</c> says.
    /// </summary>
    public static IEnumerable<(ColdDocument Document, ColdShape Shape)> StatesFor(ColdLifecycle lifecycle)
    {
        if (lifecycle == ColdLifecycle.Live)
        {
            foreach (var shape in Enum.GetValues<ColdShape>()) yield return (ColdDocument.None, shape);
            yield break;
        }

        foreach (var shape in Enum.GetValues<ColdShape>()) yield return (ColdDocument.Kept, shape);
        yield return (ColdDocument.MovedOut, ColdShape.FullyCold);
    }

    public static TheoryData<ColdIdentity, ColdLifecycle, ColdDocument, ColdShape, ColdFetch, bool, ColdSession> Cases(
        ColdLifecycle[] lifecycles, ColdSession[] sessions)
    {
        var data = new TheoryData<ColdIdentity, ColdLifecycle, ColdDocument, ColdShape, ColdFetch, bool, ColdSession>();
        foreach (var identity in SupportedIdentities)
        foreach (var lifecycle in lifecycles)
        foreach (var (document, shape) in StatesFor(lifecycle))
        foreach (var fetch in FetchesFor(identity))
        foreach (var cached in new[] { false, true })
        foreach (var session in sessions)
        {
            data.Add(identity, lifecycle, document, shape, fetch, cached, session);
        }

        return data;
    }

    /// <summary>
    ///     The writing fetches, for a stream that goes colder between the fetch and the commit. The exclusive fetch
    ///     is left out: it holds the stream's row lock, so the move would wait for the commit.
    /// </summary>
    public static TheoryData<ColdIdentity, ColdLifecycle, ColdFetch, bool> MoveBeforeTheCommitCases(
        ColdLifecycle[] lifecycles)
    {
        var data = new TheoryData<ColdIdentity, ColdLifecycle, ColdFetch, bool>();
        foreach (var identity in SupportedIdentities)
        foreach (var lifecycle in lifecycles)
        foreach (var fetch in FetchesFor(identity))
        foreach (var cached in new[] { false, true })
        {
            if (fetch is ColdFetch.FetchForExclusiveWriting or ColdFetch.FetchLatest or ColdFetch.BatchedFetchLatest
                or ColdFetch.ProjectLatest or ColdFetch.AggregateStreamAsync)
            {
                continue;
            }

            data.Add(identity, lifecycle, fetch, cached);
        }

        return data;
    }

    public static Task RunWithAMoveBeforeTheCommitAsync(ColdMatrixFixture fixture, ColdIdentity identity,
        ColdLifecycle lifecycle, ColdFetch fetch, bool cached)
    {
        var document = lifecycle == ColdLifecycle.Live ? ColdDocument.None : ColdDocument.Kept;
        var run = new ColdMatrixRun(fixture, identity, lifecycle, document, ColdShape.PartlyCold, fetch, cached,
            ColdSession.Lightweight);
        return dispatch(run, lifecycle, cached, identity, nameof(ColdMatrixRun.RunWithAMoveBeforeTheCommitAsync));
    }

    public static Task RunAsync(ColdMatrixFixture fixture, ColdIdentity identity, ColdLifecycle lifecycle,
        ColdDocument document, ColdShape shape, ColdFetch fetch, bool cached, ColdSession session)
    {
        var run = new ColdMatrixRun(fixture, identity, lifecycle, document, shape, fetch, cached, session);
        return dispatch(run, lifecycle, cached, identity, nameof(ColdMatrixRun.RunAsync));
    }

    public static Type AggregateTypeFor(ColdLifecycle lifecycle, bool cached, ColdIdentity identity) =>
        (lifecycle, cached, identity) switch
        {
            (ColdLifecycle.Live, false, ColdIdentity.String) => typeof(LiveKeyedCart),
            (ColdLifecycle.Live, true, ColdIdentity.String) => typeof(LiveKeyedCachedCart),
            (ColdLifecycle.Live, false, ColdIdentity.NaturalKey) => typeof(LiveNumberedCart),
            (ColdLifecycle.Live, true, ColdIdentity.NaturalKey) => typeof(LiveNumberedCachedCart),
            (ColdLifecycle.Inline, false, ColdIdentity.String) => typeof(InlineKeyedCart),
            (ColdLifecycle.Inline, true, ColdIdentity.String) => typeof(InlineKeyedCachedCart),
            (ColdLifecycle.Inline, false, ColdIdentity.NaturalKey) => typeof(InlineNumberedCart),
            (ColdLifecycle.Inline, true, ColdIdentity.NaturalKey) => typeof(InlineNumberedCachedCart),
            (ColdLifecycle.Async, false, ColdIdentity.String) => typeof(AsyncKeyedCart),
            (ColdLifecycle.Async, true, ColdIdentity.String) => typeof(AsyncKeyedCachedCart),
            (ColdLifecycle.Async, false, ColdIdentity.NaturalKey) => typeof(AsyncNumberedCart),
            (ColdLifecycle.Async, true, ColdIdentity.NaturalKey) => typeof(AsyncNumberedCachedCart),
            _ => throw new ArgumentOutOfRangeException(nameof(identity), $"No aggregate type for {identity}")
        };

    // Closes a generic method of the run over the case's aggregate type
    private static Task dispatch(ColdMatrixRun run, ColdLifecycle lifecycle, bool cached, ColdIdentity identity,
        string method) =>
        (Task)typeof(ColdMatrixRun).GetMethod(method)!
            .MakeGenericMethod(AggregateTypeFor(lifecycle, cached, identity))
            .Invoke(run, null)!;
}

/// <summary>One permutation of the matrix.</summary>
public class ColdMatrixRun(
    ColdMatrixFixture fixture,
    ColdIdentity identity,
    ColdLifecycle lifecycle,
    ColdDocument document,
    ColdShape shape,
    ColdFetch fetch,
    bool cached,
    ColdSession sessionType)
{
    public string Key { get; } = Guid.NewGuid().ToString();
    public MatrixOrderNumber Number => new("ORD-" + Key);

    private bool writes => fetch is ColdFetch.FetchForWriting or ColdFetch.FetchForWritingWithExpectedVersion
        or ColdFetch.FetchForExclusiveWriting or ColdFetch.BatchedFetchForWriting
        or ColdFetch.BatchedFetchForWritingWithExpectedVersion or ColdFetch.FetchManyForWriting;

    public async Task RunAsync<T>() where T : MatrixCart
    {
        await ArrangeAsync<T>();

        await using var session = fixture.OpenSession(sessionType);

        if (!writes)
        {
            var aggregate = await ReadAsync<T>(session);
            ShouldBeTheWholeStream(aggregate);
            return;
        }

        var stream = await FetchForWritingAsync<T>(session);
        stream.StartingVersion.ShouldBe(4, "the stream's version, whatever part of it is cold");
        ShouldBeTheWholeStream(stream.Aggregate);

        // FetchLatest right after FetchForWriting, which UseIdentityMapForAggregates answers from the fetched stream
        ShouldBeTheWholeStream(await session.Events.FetchLatest<T>(Key));

        // An append after the cold move is an ordinary append
        stream.AppendOne(new MatrixItemAdded("d"));
        await session.SaveChangesAsync();

        await ShouldBeTheWholeStreamAfterTheAppendAsync<T>();
    }

    /// <summary>
    ///     The stream goes colder between the fetch and the commit. A fetch that folded the cold part remembered its
    ///     boundary, so the commit fails and the caller retries; a fetch that read a stored document, which already
    ///     holds the cold part, did not need it, and its commit goes through.
    /// </summary>
    public async Task RunWithAMoveBeforeTheCommitAsync<T>() where T : MatrixCart
    {
        await ArrangeAsync<T>();

        await using var session = fixture.OpenSession(sessionType);
        var stream = await FetchForWritingAsync<T>(session);
        ShouldBeTheWholeStream(stream.Aggregate);
        stream.AppendOne(new MatrixItemAdded("d"));

        await fixture.CatchUpAsync();
        await fixture.MoveToColdAsync(Key, 3);

        if (lifecycle == ColdLifecycle.Live)
        {
            var changed = await Should.ThrowAsync<Marten.Exceptions.ColdStreamChangedException>(
                () => session.SaveChangesAsync());
            changed.Streams.ShouldHaveSingleItem().ShouldContain(Key);

            (await fixture.Store.QuerySession(fixture.TenantId).Events.FetchStreamStateAsync(Key))
                .ShouldNotBeNull().Version.ShouldBe(4, "nothing was appended");
            return;
        }

        await session.SaveChangesAsync();
        await ShouldBeTheWholeStreamAfterTheAppendAsync<T>();
    }

    /// <summary>The stream, its cold move and what became of the stored document.</summary>
    public async Task ArrangeAsync<T>() where T : MatrixCart
    {
        await using (var session = fixture.OpenSession())
        {
            session.Events.StartStream<T>(Key, new MatrixOpened("anne", Number), new MatrixItemAdded("a"),
                new MatrixItemAdded("b"));
            await session.SaveChangesAsync();
        }

        if (cached)
        {
            // Puts the aggregate at version 3 in the write cache, below a fully cold stream's boundary
            await fixture.CatchUpAsync();
            await using var session = fixture.OpenSession();
            await session.Events.FetchForWriting<T>(Key);
            await session.SaveChangesAsync();
        }

        switch (shape)
        {
            case ColdShape.PartlyCold:
                await appendAsync("c");
                await fixture.CatchUpAsync();
                await fixture.MoveToColdAsync(Key, 2);
                break;

            case ColdShape.FullyCold:
                await appendAsync("c");
                await fixture.CatchUpAsync();
                await fixture.MoveToColdAsync(Key, 4);
                break;

            case ColdShape.ColdTwice:
                await fixture.CatchUpAsync();
                await fixture.MoveToColdAsync(Key, 2);
                await appendAsync("c");
                await fixture.CatchUpAsync();
                await fixture.MoveToColdAsync(Key, 3);
                break;
        }

        if (document == ColdDocument.MovedOut)
        {
            await using var session = fixture.OpenSession();
            session.HardDelete<T>(Key);
            await session.SaveChangesAsync();
        }
    }

    private async Task appendAsync(string item)
    {
        await using var session = fixture.OpenSession();
        session.Events.Append(Key, new MatrixItemAdded(item));
        await session.SaveChangesAsync();
    }

    public async Task<IEventStream<T>> FetchForWritingAsync<T>(IDocumentSession session) where T : MatrixCart
    {
        if (identity == ColdIdentity.NaturalKey)
        {
            return fetch switch
            {
                ColdFetch.FetchForWriting => await session.Events.FetchForWriting<T, MatrixOrderNumber>(Number),
                ColdFetch.FetchForExclusiveWriting =>
                    await session.Events.FetchForExclusiveWriting<T, MatrixOrderNumber>(Number),
                _ => throw new ArgumentOutOfRangeException(nameof(fetch), $"{fetch} has no natural-key form")
            };
        }

        switch (fetch)
        {
            case ColdFetch.FetchForWriting:
                return await session.Events.FetchForWriting<T>(Key);

            case ColdFetch.FetchForWritingWithExpectedVersion:
                return await session.Events.FetchForWriting<T>(Key, 4);

            case ColdFetch.FetchForExclusiveWriting:
                return await session.Events.FetchForExclusiveWriting<T>(Key);

            case ColdFetch.BatchedFetchForWriting:
            {
                var batch = session.CreateBatchQuery();
                var pending = batch.Events.FetchForWriting<T>(Key);
                await batch.Execute();
                return await pending;
            }

            case ColdFetch.BatchedFetchForWritingWithExpectedVersion:
            {
                var batch = session.CreateBatchQuery();
                var pending = batch.Events.FetchForWriting<T>(Key, 4);
                await batch.Execute();
                return await pending;
            }

            case ColdFetch.FetchManyForWriting:
                return (await session.Events.FetchManyForWriting<T>([Key])).ShouldHaveSingleItem();

            default:
                throw new ArgumentOutOfRangeException(nameof(fetch), $"{fetch} does not fetch for writing");
        }
    }

    public async Task<T?> ReadAsync<T>(IDocumentSession session) where T : MatrixCart
    {
        if (identity == ColdIdentity.NaturalKey)
        {
            return fetch == ColdFetch.FetchLatest
                ? await session.Events.FetchLatest<T, MatrixOrderNumber>(Number)
                : throw new ArgumentOutOfRangeException(nameof(fetch), $"{fetch} has no natural-key form");
        }

        switch (fetch)
        {
            case ColdFetch.FetchLatest:
                return await session.Events.FetchLatest<T>(Key);

            case ColdFetch.BatchedFetchLatest:
            {
                var batch = session.CreateBatchQuery();
                var pending = batch.Events.FetchLatest<T>(Key);
                await batch.Execute();
                return await pending;
            }

            case ColdFetch.ProjectLatest:
                return await session.Events.ProjectLatest<T>(Key);

            case ColdFetch.AggregateStreamAsync:
                return await session.Events.AggregateStreamAsync<T>(Key);

            default:
                throw new ArgumentOutOfRangeException(nameof(fetch), $"{fetch} does not read");
        }
    }

    public static void ShouldBeTheWholeStream(MatrixCart? aggregate, params string[] more)
    {
        aggregate.ShouldNotBeNull("the aggregate of a stream with a cold part");
        aggregate.Customer.ShouldBe("anne", "the cold part was folded");
        string.Join(", ", aggregate.Items).ShouldBe(string.Join(", ", ColdMatrix.Expected.Concat(more)),
            "every event, cold and warm, once and in order");
    }

    private async Task ShouldBeTheWholeStreamAfterTheAppendAsync<T>() where T : MatrixCart
    {
        await using (var session = fixture.OpenSession())
        {
            // By stream key: the natural-key FetchLatest of an Async aggregate reads the stored document as it is
            var latest = await session.Events.FetchLatest<T>(Key);
            ShouldBeTheWholeStream(latest, "d");
            (await session.Events.FetchStreamStateAsync(Key)).ShouldNotBeNull().Version.ShouldBe(5);
        }

        if (lifecycle == ColdLifecycle.Live) return;

        // The projection itself: a document that went out with the events starts again from the cold part
        await fixture.CatchUpAsync();
        await using var query = fixture.Store.QuerySession(fixture.TenantId);
        ShouldBeTheWholeStream(await query.LoadAsync<T>(Key), "d");
    }
}
