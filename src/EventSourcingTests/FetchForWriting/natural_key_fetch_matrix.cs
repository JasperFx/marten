using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

// Every fetch by natural key, for every lifecycle, for every shape of key. Each class is one key shape; each
// case is one lifecycle and one kind of fetch. The race cases are in async_fetch_reads_in_a_safe_order_by_natural_key.

public readonly record struct BinKey(int Value);

public record BinKeyed(BinKey Key);

public class IntKeyedAggregate
{
    public Guid Id { get; set; }

    [NaturalKey]
    public BinKey Key { get; set; }

    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }

    [NaturalKeySource]
    public void Apply(BinKeyed e)
    {
        Key = e.Key;
        ACount++;
    }

    public void Apply(BEvent _) => BCount++;
    public void Apply(CEvent _) => CCount++;
    public void Apply(DEvent _) => DCount++;
}

public record ClassKey(string Value);

public record ClassKeyed(ClassKey Key);

public class ClassKeyedAggregate
{
    public Guid Id { get; set; }

    [NaturalKey]
    public ClassKey Key { get; set; } = null!;

    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }

    [NaturalKeySource]
    public void Apply(ClassKeyed e)
    {
        Key = e.Key;
        ACount++;
    }

    public void Apply(BEvent _) => BCount++;
    public void Apply(CEvent _) => CCount++;
    public void Apply(DEvent _) => DCount++;
}

/// <summary>The key declared <c>TKey?</c> on the aggregate, and fetched by <c>TKey</c>.</summary>
public class NullableKeyedAggregate
{
    public Guid Id { get; set; }

    [NaturalKey]
    public RaceKey? Key { get; set; }

    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }

    [NaturalKeySource]
    public void Apply(KeyedAEvent e)
    {
        Key = e.Key;
        ACount++;
    }

    public void Apply(BEvent _) => BCount++;
    public void Apply(CEvent _) => CCount++;
    public void Apply(DEvent _) => DCount++;
}

/// <summary>The owner of a mapped key on a store with string stream ids.</summary>
public class RaceKeyOwnerAsString
{
    public string Id { get; set; } = string.Empty;

    [NaturalKey]
    public RaceKey Key { get; set; }

    [NaturalKeySource]
    public void Apply(KeyedAEvent e) => Key = e.Key;
}

/// <summary>Found through <see cref="RaceKeyOwnerAsString" />'s lookup, on a store with string stream ids.</summary>
public class MappedKeySimpleAggregateAsString
{
    public string Id { get; set; } = string.Empty;

    [MappedNaturalKey<RaceKeyOwnerAsString>]
    public RaceKey Key { get; set; }

    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }

    public void Apply(KeyedAEvent e)
    {
        Key = e.Key;
        ACount++;
    }

    public void Apply(BEvent _) => BCount++;
    public void Apply(CEvent _) => CCount++;
    public void Apply(DEvent _) => DCount++;
}

public enum MatrixLifecycle
{
    Inline,
    Async,

    /// <summary>Async, with the daemon caught up before the fetch, so the fetch starts from a snapshot.</summary>
    AsyncCaughtUp,
    Live
}

public enum MatrixFetch
{
    ForWriting,
    ForWritingAtVersion,
    ForWritingAtAStaleVersion,
    ForExclusiveWriting,
    ForWritingInsideCallerTransaction,
    ManyForWriting,
    Latest,
    BatchedForWriting,
    BatchedForWritingAtVersion,
    BatchedForWritingAtAStaleVersion,
    BatchedForExclusiveWriting,
    BatchedLatest
}

/// <summary>One aggregate and key type, and how to start its streams and fetch them by key.</summary>
public abstract class MatrixSubject
{
    public abstract void Register(StoreOptions opts, MatrixLifecycle lifecycle);
    public abstract object NewStreamId();

    /// <summary>A key no stream has.</summary>
    public abstract object UnknownKey();

    public abstract object KeyFor(object streamId);
    public abstract void Start(IDocumentSession session, object streamId, params char[] letters);

    public abstract Task<(object? StreamId, async_fetch_reads_in_a_safe_order.Found? Found)> FetchAsync(
        IDocumentSession session, MatrixFetch fetch, object key, long expectedVersion, bool appendD,
        CancellationToken ct);

    /// <summary>FetchManyForWriting by these keys, appending a D event to each stream that was found.</summary>
    public abstract Task<IReadOnlyList<(object? StreamId, async_fetch_reads_in_a_safe_order.Found Found)>>
        FetchManyAsync(IDocumentSession session, object[] keys, CancellationToken ct);
}

public sealed class MatrixSubject<T, TKey>(
    Action<StoreOptions> configure,
    Func<object> newStreamId,
    Func<object, TKey> keyFor,
    Func<TKey> unknownKey,
    Func<TKey, object> keyedA,
    Func<T, async_fetch_reads_in_a_safe_order.Found> countsOf,
    Func<T, object> streamIdOf): MatrixSubject where T : class where TKey : notnull
{
    public override void Register(StoreOptions opts, MatrixLifecycle lifecycle)
    {
        configure(opts);
        switch (lifecycle)
        {
            case MatrixLifecycle.Inline:
                opts.Projections.Snapshot<T>(SnapshotLifecycle.Inline);
                break;
            case MatrixLifecycle.Async:
            case MatrixLifecycle.AsyncCaughtUp:
                opts.Projections.Snapshot<T>(SnapshotLifecycle.Async);
                break;
            case MatrixLifecycle.Live:
                opts.Projections.LiveStreamAggregation<T>();
                break;
        }
    }

    public override object NewStreamId() => newStreamId();
    public override object UnknownKey() => unknownKey();
    public override object KeyFor(object streamId) => keyFor(streamId);

    public override void Start(IDocumentSession session, object streamId, params char[] letters)
    {
        var events = letters.Select(x => x switch
        {
            'A' => keyedA(keyFor(streamId)),
            'B' => (object)new BEvent(),
            'C' => new CEvent(),
            'D' => new DEvent(),
            _ => throw new ArgumentOutOfRangeException(nameof(letters))
        }).ToArray();

        if (streamId is Guid guid) session.Events.StartStream<T>(guid, events);
        else session.Events.StartStream<T>((string)streamId, events);
    }

    private (object?, async_fetch_reads_in_a_safe_order.Found?) found(T? aggregate, long? version) =>
        aggregate == null ? (null, null) : (streamIdOf(aggregate), countsOf(aggregate) with { Version = version });

    private (object?, async_fetch_reads_in_a_safe_order.Found?) found(IEventStream<T> stream, bool appendD)
    {
        (object?, async_fetch_reads_in_a_safe_order.Found?) result = stream.Aggregate == null
            ? (null, new async_fetch_reads_in_a_safe_order.Found(0, 0, 0, 0, stream.CurrentVersion ?? 0))
            : (stream.Key ?? (object)stream.Id, countsOf(stream.Aggregate) with { Version = stream.CurrentVersion });

        // After reading the version, which the append moves on
        if (appendD) stream.AppendOne(new DEvent());
        return result;
    }

    public override async Task<(object? StreamId, async_fetch_reads_in_a_safe_order.Found? Found)> FetchAsync(
        IDocumentSession session, MatrixFetch fetch, object key, long expectedVersion, bool appendD,
        CancellationToken ct)
    {
        var k = (TKey)key;
        var events = session.Events;

        switch (fetch)
        {
            case MatrixFetch.ForWriting:
            case MatrixFetch.ForWritingInsideCallerTransaction:
                return found(await events.FetchForWriting<T, TKey>(k, ct), appendD);
            case MatrixFetch.ForWritingAtVersion:
            case MatrixFetch.ForWritingAtAStaleVersion:
                return found(await events.FetchForWriting<T, TKey>(k, expectedVersion, ct), appendD);
            case MatrixFetch.ForExclusiveWriting:
                return found(await events.FetchForExclusiveWriting<T, TKey>(k, ct), appendD);
            case MatrixFetch.ManyForWriting:
                var many = await events.FetchManyForWriting<T, TKey>([k], ct);
                many.Count.ShouldBe(1);
                return found(many[0], appendD);
            case MatrixFetch.Latest:
                return found(await events.FetchLatest<T, TKey>(k, ct), null);
        }

        var batch = session.CreateBatchQuery();
        var before = batch.Load<Target>(Guid.NewGuid());

        Task<(object?, async_fetch_reads_in_a_safe_order.Found?)> query = fetch switch
        {
            MatrixFetch.BatchedForWriting => streamed(batch.Events.FetchForWriting<T, TKey>(k), appendD),
            MatrixFetch.BatchedForWritingAtVersion or MatrixFetch.BatchedForWritingAtAStaleVersion =>
                streamed(batch.Events.FetchForWriting<T, TKey>(k, expectedVersion), appendD),
            MatrixFetch.BatchedForExclusiveWriting => streamed(batch.Events.FetchForExclusiveWriting<T, TKey>(k), appendD),
            MatrixFetch.BatchedLatest => latest(batch.Events.FetchLatest<T, TKey>(k)),
            _ => throw new ArgumentOutOfRangeException(nameof(fetch))
        };

        await batch.Execute(ct);
        (await before).ShouldBeNull();
        return await query;
    }

    private async Task<(object?, async_fetch_reads_in_a_safe_order.Found?)> streamed(Task<IEventStream<T>> fetch,
        bool appendD) => found(await fetch, appendD);

    public override async Task<IReadOnlyList<(object? StreamId, async_fetch_reads_in_a_safe_order.Found Found)>>
        FetchManyAsync(IDocumentSession session, object[] keys, CancellationToken ct)
    {
        var streams = await session.Events.FetchManyForWriting<T, TKey>(keys.Cast<TKey>().ToArray(), ct);
        return streams.Select(x => found(x, appendD: x.Aggregate != null)).Select(x => (x.Item1, x.Item2!.Value))
            .ToArray();
    }

    private async Task<(object?, async_fetch_reads_in_a_safe_order.Found?)> latest(Task<T?> fetch) =>
        found(await fetch, null);
}

public sealed record MatrixShape(MatrixSubject Subject, Action<StoreOptions>? Configure = null,
    string? Tenant = null, string? OtherTenant = null);

public abstract class natural_key_fetch_matrix: OneOffConfigurationsContext
{
    private readonly MatrixShape _shape;

    protected natural_key_fetch_matrix(MatrixShape shape)
    {
        _shape = shape;
    }

    public static IEnumerable<object[]> Cases() =>
        from lifecycle in Enum.GetValues<MatrixLifecycle>()
        from fetch in Enum.GetValues<MatrixFetch>()
        select new object[] { lifecycle, fetch };

    private static bool isWrite(MatrixFetch fetch) => fetch is not (MatrixFetch.Latest or MatrixFetch.BatchedLatest);

    private static bool expectsAVersion(MatrixFetch fetch) => fetch is MatrixFetch.ForWritingAtVersion
        or MatrixFetch.ForWritingAtAStaleVersion or MatrixFetch.BatchedForWritingAtVersion
        or MatrixFetch.BatchedForWritingAtAStaleVersion;

    private static bool isStale(MatrixFetch fetch) =>
        fetch is MatrixFetch.ForWritingAtAStaleVersion or MatrixFetch.BatchedForWritingAtAStaleVersion;

    private void configure(MatrixLifecycle lifecycle) => StoreOptions(opts =>
    {
        _shape.Configure?.Invoke(opts);
        _shape.Subject.Register(opts, lifecycle);
    });

    private IDocumentSession sessionFor(string? tenant) =>
        tenant == null ? theStore.LightweightSession() : theStore.LightweightSession(tenant);

    private async Task<object> startAsync(string? tenant, object streamId, params char[] letters)
    {
        await using var session = sessionFor(tenant);
        _shape.Subject.Start(session, streamId, letters);
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return streamId;
    }

    private async Task catchTheDaemonUpAsync()
    {
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.CatchUpAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Runs the fetch in a session of the shape's tenant, appending a D event and committing when it is a
    /// fetch for writing and <paramref name="appendD" /> is set.
    /// </summary>
    private async Task<(object? StreamId, async_fetch_reads_in_a_safe_order.Found? Found)> fetchAsync(
        MatrixFetch fetch, object key, long expectedVersion, bool appendD)
    {
        var ct = TestContext.Current.CancellationToken;

        if (fetch == MatrixFetch.ForWritingInsideCallerTransaction)
        {
            await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var options = SessionOptions.ForTransaction(tx);
            if (_shape.Tenant != null) options.TenantId = _shape.Tenant;

            await using var inTx = theStore.LightweightSession(options);
            var requests = inTx.RequestCount;
            var result = await _shape.Subject.FetchAsync(inTx, fetch, key, expectedVersion, appendD, ct);
            (inTx.RequestCount - requests).ShouldBe(1, "one round trip");
            await inTx.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return result;
        }

        await using var session = sessionFor(_shape.Tenant);
        var before = session.RequestCount;
        var fetched = await _shape.Subject.FetchAsync(session, fetch, key, expectedVersion, appendD, ct);

        // Resolving the key and reading the aggregate is one command, batched or not
        (session.RequestCount - before).ShouldBe(1, "one round trip");
        if (appendD && isWrite(fetch)) await session.SaveChangesAsync(ct);
        return fetched;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task fetches_the_stream_by_its_key(MatrixLifecycle lifecycle, MatrixFetch fetch)
    {
        configure(lifecycle);

        // A stream in another tenant with the same key: anything read across tenants shows in the counts
        var streamId = _shape.Subject.NewStreamId();
        if (_shape.OtherTenant != null) await startAsync(_shape.OtherTenant, streamId, 'A', 'B', 'B', 'B', 'B');

        await startAsync(_shape.Tenant, streamId, 'A', 'B', 'C');
        if (lifecycle == MatrixLifecycle.AsyncCaughtUp) await catchTheDaemonUpAsync();

        var key = _shape.Subject.KeyFor(streamId);

        if (isStale(fetch))
        {
            var ex = await Should.ThrowAsync<ConcurrencyException>(() => fetchAsync(fetch, key, 2, false));
            ex.Message.ShouldContain("but was 3");
            return;
        }

        var (foundId, found) = await fetchAsync(fetch, key, 3, appendD: true);

        foundId.ShouldBe(streamId);
        found.ShouldNotBeNull();
        found.Value.ShouldBe(new async_fetch_reads_in_a_safe_order.Found(1, 1, 1, 0,
            isWrite(fetch) ? 3 : null));

        if (!isWrite(fetch)) return;

        // The D event was appended to the stream the key named
        var (_, latest) = await fetchAsync(MatrixFetch.Latest, key, 0, false);
        latest.ShouldNotBeNull();
        latest.Value.D.ShouldBe(1);
        latest.Value.B.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task an_unknown_key(MatrixLifecycle lifecycle, MatrixFetch fetch)
    {
        configure(lifecycle);
        await startAsync(_shape.Tenant, _shape.Subject.NewStreamId(), 'A', 'B', 'C');
        if (lifecycle == MatrixLifecycle.AsyncCaughtUp) await catchTheDaemonUpAsync();

        var key = _shape.Subject.UnknownKey();

        if (expectsAVersion(fetch))
        {
            var ex = await Should.ThrowAsync<ConcurrencyException>(() => fetchAsync(fetch, key, 3, false));
            ex.Message.ShouldContain("but was 0");
            return;
        }

        var (foundId, found) = await fetchAsync(fetch, key, 0, false);
        foundId.ShouldBeNull();

        if (isWrite(fetch))
        {
            // A new stream in its place, with nothing to fold
            found.ShouldBe(new async_fetch_reads_in_a_safe_order.Found(0, 0, 0, 0, 0));
        }
        else
        {
            found.ShouldBeNull();
        }
    }

    public static IEnumerable<object[]> Lifecycles() =>
        Enum.GetValues<MatrixLifecycle>().Select(x => new object[] { x });

    [Theory]
    [MemberData(nameof(Lifecycles))]
    public async Task fetches_many_streams_by_their_keys(MatrixLifecycle lifecycle)
    {
        configure(lifecycle);
        var ct = TestContext.Current.CancellationToken;

        var first = _shape.Subject.NewStreamId();
        var second = _shape.Subject.NewStreamId();
        if (_shape.OtherTenant != null) await startAsync(_shape.OtherTenant, first, 'A', 'B', 'B', 'B', 'B');
        await startAsync(_shape.Tenant, first, 'A', 'B');
        await startAsync(_shape.Tenant, second, 'A', 'C', 'C');
        if (lifecycle == MatrixLifecycle.AsyncCaughtUp) await catchTheDaemonUpAsync();

        await using (var session = sessionFor(_shape.Tenant))
        {
            var before = session.RequestCount;
            var keys = new[] { _shape.Subject.KeyFor(second), _shape.Subject.UnknownKey(), _shape.Subject.KeyFor(first) };
            var fetched = await _shape.Subject.FetchManyAsync(session, keys, ct);
            (session.RequestCount - before).ShouldBe(1, "one round trip");

            // In the order of the keys, each at its own version, the unknown key as a new stream in its place
            fetched.Select(x => x.StreamId).ShouldBe([second, null, first]);
            fetched[0].Found.ShouldBe(new async_fetch_reads_in_a_safe_order.Found(1, 0, 2, 0, 3));
            fetched[1].Found.ShouldBe(new async_fetch_reads_in_a_safe_order.Found(0, 0, 0, 0, 0));
            fetched[2].Found.ShouldBe(new async_fetch_reads_in_a_safe_order.Found(1, 1, 0, 0, 2));

            await session.SaveChangesAsync(ct);
        }

        // Each D event went to the stream its key named
        var (_, latestFirst) = await fetchAsync(MatrixFetch.Latest, _shape.Subject.KeyFor(first), 0, false);
        latestFirst!.Value.ShouldBe(new async_fetch_reads_in_a_safe_order.Found(1, 1, 0, 1, null));
        var (_, latestSecond) = await fetchAsync(MatrixFetch.Latest, _shape.Subject.KeyFor(second), 0, false);
        latestSecond!.Value.ShouldBe(new async_fetch_reads_in_a_safe_order.Found(1, 0, 2, 1, null));
    }
}

internal static class MatrixSubjects
{
    private static async_fetch_reads_in_a_safe_order.Found counts(int a, int b, int c, int d) => new(a, b, c, d, null);

    public static readonly MatrixSubject Owned = new MatrixSubject<KeyedSimpleAggregate, RaceKey>(
        _ => { }, () => Guid.NewGuid(), RaceKey.For, () => new RaceKey("K-NONE"), k => new KeyedAEvent(k),
        x => counts(x.ACount, x.BCount, x.CCount, x.DCount), x => x.Id);

    public static readonly MatrixSubject OwnedInt = new MatrixSubject<IntKeyedAggregate, BinKey>(
        _ => { }, () => Guid.NewGuid(), id => new BinKey((id.GetHashCode() & 0x3fffffff) + 1), () => new BinKey(-1),
        k => new BinKeyed(k), x => counts(x.ACount, x.BCount, x.CCount, x.DCount), x => x.Id);

    public static readonly MatrixSubject OwnedClass = new MatrixSubject<ClassKeyedAggregate, ClassKey>(
        _ => { }, () => Guid.NewGuid(), id => new ClassKey("C-" + id), () => new ClassKey("C-NONE"),
        k => new ClassKeyed(k), x => counts(x.ACount, x.BCount, x.CCount, x.DCount), x => x.Id);

    public static readonly MatrixSubject NullableMember = new MatrixSubject<NullableKeyedAggregate, RaceKey>(
        _ => { }, () => Guid.NewGuid(), RaceKey.For, () => new RaceKey("K-NONE"), k => new KeyedAEvent(k),
        x => counts(x.ACount, x.BCount, x.CCount, x.DCount), x => x.Id);

    public static readonly MatrixSubject Mapped = new MatrixSubject<MappedKeySimpleAggregate, RaceKey>(
        opts => opts.Projections.Snapshot<RaceKeyOwner>(SnapshotLifecycle.Inline),
        () => Guid.NewGuid(), RaceKey.For, () => new RaceKey("K-NONE"), k => new KeyedAEvent(k),
        x => counts(x.ACount, x.BCount, x.CCount, x.DCount), x => x.Id);

    public static readonly MatrixSubject OnStringStreams = new MatrixSubject<KeyedSimpleAggregateAsString, RaceKey>(
        opts => opts.Events.StreamIdentity = StreamIdentity.AsString,
        () => Guid.NewGuid().ToString(), RaceKey.For, () => new RaceKey("K-NONE"), k => new KeyedAEvent(k),
        x => counts(x.ACount, x.BCount, x.CCount, x.DCount), x => x.Id);

    public static readonly MatrixSubject MappedOnStringStreams = new MatrixSubject<MappedKeySimpleAggregateAsString, RaceKey>(
        opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<RaceKeyOwnerAsString>(SnapshotLifecycle.Inline);
        },
        () => Guid.NewGuid().ToString(), RaceKey.For, () => new RaceKey("K-NONE"), k => new KeyedAEvent(k),
        x => counts(x.ACount, x.BCount, x.CCount, x.DCount), x => x.Id);

    public static readonly MatrixSubject StrongTypedId = new MatrixSubject<KeyedStrongTypedAggregate, RaceKey>(
        opts => opts.RegisterValueType<StrongTypedAggregateId>(),
        () => Guid.NewGuid(), RaceKey.For, () => new RaceKey("K-NONE"), k => new KeyedAEvent(k),
        x => counts(x.ACount, x.BCount, x.CCount, x.DCount), x => x.Id.Value);
}

public class natural_key_fetch_matrix_owned_key(): natural_key_fetch_matrix(new MatrixShape(MatrixSubjects.Owned));

public class natural_key_fetch_matrix_owned_int_key(): natural_key_fetch_matrix(new MatrixShape(MatrixSubjects.OwnedInt));

public class natural_key_fetch_matrix_owned_class_key(): natural_key_fetch_matrix(new MatrixShape(MatrixSubjects.OwnedClass));

public class natural_key_fetch_matrix_nullable_key_member(): natural_key_fetch_matrix(new MatrixShape(MatrixSubjects.NullableMember));

public class natural_key_fetch_matrix_mapped_key(): natural_key_fetch_matrix(new MatrixShape(MatrixSubjects.Mapped));

public class natural_key_fetch_matrix_on_string_streams(): natural_key_fetch_matrix(new MatrixShape(MatrixSubjects.OnStringStreams));

public class natural_key_fetch_matrix_mapped_key_on_string_streams(): natural_key_fetch_matrix(new MatrixShape(MatrixSubjects.MappedOnStringStreams));

public class natural_key_fetch_matrix_strong_typed_id(): natural_key_fetch_matrix(new MatrixShape(MatrixSubjects.StrongTypedId));

public class natural_key_fetch_matrix_conjoined_tenancy(): natural_key_fetch_matrix(new MatrixShape(MatrixSubjects.Owned,
    opts =>
    {
        opts.Policies.AllDocumentsAreMultiTenanted();
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
    }, Tenant: "tenant-a", OtherTenant: "tenant-b"));
