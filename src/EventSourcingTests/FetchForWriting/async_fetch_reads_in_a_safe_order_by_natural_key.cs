using System;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Storage;
using Marten.Testing.Documents;
using Shouldly;

namespace EventSourcingTests.FetchForWriting;

// async_fetch_reads_in_a_safe_order's scenarios, fetched by natural key. The stream id still names the stream
// the scenarios start, append to and delete the snapshot of; the fetch goes by the key derived from it.

public readonly record struct RaceKey(string Value)
{
    public static RaceKey For(object streamId) => new("K-" + streamId);
}

/// <summary>The first event of every stream in these tests, carrying the stream's natural key.</summary>
public record KeyedAEvent(RaceKey Key);

public class KeyedSimpleAggregate
{
    public Guid Id { get; set; }

    [NaturalKey]
    public RaceKey Key { get; set; }

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

public class KeyedSimpleAggregateAsString
{
    public string Id { get; set; } = string.Empty;

    [NaturalKey]
    public RaceKey Key { get; set; }

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

public class KeyedStrongTypedAggregate: IRevisioned
{
    public int Version { get; set; }
    public StrongTypedAggregateId Id { get; set; }

    [NaturalKey]
    public RaceKey Key { get; set; }

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

/// <summary>Owns the key that <see cref="MappedKeySimpleAggregate" /> maps.</summary>
public class RaceKeyOwner
{
    public Guid Id { get; set; }

    [NaturalKey]
    public RaceKey Key { get; set; }

    [NaturalKeySource]
    public void Apply(KeyedAEvent e) => Key = e.Key;
}

public class MappedKeySimpleAggregate
{
    public Guid Id { get; set; }

    [MappedNaturalKey<RaceKeyOwner>]
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

/// <summary>
/// Drives an aggregate through every kind of fetch by its natural key. The 'A' event is the one that sets the
/// key, so it is written as <see cref="KeyedAEvent" /> carrying the key derived from the stream id.
/// </summary>
public sealed class NaturalKeyFetchSubject<T>(
    string snapshotTable,
    Func<object> newId,
    Func<T, async_fetch_reads_in_a_safe_order.Found> countsOf): FetchSubject where T : class
{
    public override string SnapshotTable => snapshotTable;
    public override bool ByNaturalKey => true;

    // No StreamLatestJson by natural key
    public override bool Supports(async_fetch_reads_in_a_safe_order.Fetch fetch) =>
        fetch != async_fetch_reads_in_a_safe_order.Fetch.StreamLatestJson;

    public override object NewId() => newId();

    // The stream id is not known here, so the 'A' event is keyed in Start and Append
    public override object Event(char letter) => letter switch
    {
        'A' => new AEvent(),
        'B' => new BEvent(),
        'C' => new CEvent(),
        'D' => new DEvent(),
        _ => throw new ArgumentOutOfRangeException(nameof(letter))
    };

    private static object[] keyed(object id, object[] events)
    {
        var copy = (object[])events.Clone();
        for (var i = 0; i < copy.Length; i++)
        {
            if (copy[i] is AEvent) copy[i] = new KeyedAEvent(RaceKey.For(id));
        }

        return copy;
    }

    public override void Start(IDocumentSession session, object id, object[] events)
    {
        if (id is Guid guid) session.Events.StartStream<T>(guid, keyed(id, events));
        else session.Events.StartStream<T>((string)id, keyed(id, events));
    }

    public override void Append(IDocumentSession session, object id, object[] events)
    {
        if (id is Guid guid) session.Events.Append(guid, keyed(id, events));
        else session.Events.Append((string)id, keyed(id, events));
    }

    private async_fetch_reads_in_a_safe_order.Found? found(T? aggregate, long? version) =>
        aggregate == null ? null : countsOf(aggregate) with { Version = version };

    private async_fetch_reads_in_a_safe_order.Found? found(IEventStream<T> stream) =>
        found(stream.Aggregate, stream.CurrentVersion);

    public override async Task<async_fetch_reads_in_a_safe_order.Found?> FetchAsync(IDocumentStore store,
        IDocumentSession session, async_fetch_reads_in_a_safe_order.Fetch fetch, object id, long expectedVersion,
        CancellationToken ct)
    {
        var events = session.Events;
        var key = RaceKey.For(id);

        switch (fetch)
        {
            case async_fetch_reads_in_a_safe_order.Fetch.ForWriting:
            case async_fetch_reads_in_a_safe_order.Fetch.ForWritingInsideCallerTransaction:
                return found(await events.FetchForWriting<T, RaceKey>(key, ct));

            case async_fetch_reads_in_a_safe_order.Fetch.ForWritingAtVersion:
                return found(await events.FetchForWriting<T, RaceKey>(key, expectedVersion, ct));

            case async_fetch_reads_in_a_safe_order.Fetch.ForExclusiveWriting:
                return found(await events.FetchForExclusiveWriting<T, RaceKey>(key, ct));

            case async_fetch_reads_in_a_safe_order.Fetch.Latest:
                return found(await events.FetchLatest<T, RaceKey>(key, ct), null);
        }

        var batch = session.CreateBatchQuery();
        var before = batch.Load<Target>(Guid.NewGuid());

        Task<async_fetch_reads_in_a_safe_order.Found?> query = fetch switch
        {
            async_fetch_reads_in_a_safe_order.Fetch.BatchedForWriting =>
                streamed(batch.Events.FetchForWriting<T, RaceKey>(key)),
            async_fetch_reads_in_a_safe_order.Fetch.BatchedForWritingAtVersion =>
                streamed(batch.Events.FetchForWriting<T, RaceKey>(key, expectedVersion)),
            async_fetch_reads_in_a_safe_order.Fetch.BatchedLatest =>
                latest(batch.Events.FetchLatest<T, RaceKey>(key)),
            _ => throw new ArgumentOutOfRangeException(nameof(fetch))
        };

        await batch.Execute(ct);
        (await before).ShouldBeNull();
        return await query;
    }

    private async Task<async_fetch_reads_in_a_safe_order.Found?> streamed(Task<IEventStream<T>> fetch) =>
        found(await fetch);

    private async Task<async_fetch_reads_in_a_safe_order.Found?> latest(Task<T?> fetch) =>
        found(await fetch, null);
}

internal static class NaturalKeyStoreShapes
{
    public static readonly FetchSubject Keyed = new NaturalKeyFetchSubject<KeyedSimpleAggregate>(
        "mt_doc_keyedsimpleaggregate", () => Guid.NewGuid(),
        x => new(x.ACount, x.BCount, x.CCount, x.DCount, null));

    public static readonly FetchSubject KeyedAsString = new NaturalKeyFetchSubject<KeyedSimpleAggregateAsString>(
        "mt_doc_keyedsimpleaggregateasstring", () => Guid.NewGuid().ToString(),
        x => new(x.ACount, x.BCount, x.CCount, x.DCount, null));

    public static readonly FetchSubject KeyedStrongTyped = new NaturalKeyFetchSubject<KeyedStrongTypedAggregate>(
        "mt_doc_keyedstrongtypedaggregate", () => Guid.NewGuid(),
        x => new(x.ACount, x.BCount, x.CCount, x.DCount, null));

    public static readonly FetchSubject Mapped = new NaturalKeyFetchSubject<MappedKeySimpleAggregate>(
        "mt_doc_mappedkeysimpleaggregate", () => Guid.NewGuid(),
        x => new(x.ACount, x.BCount, x.CCount, x.DCount, null));
}

public class async_fetch_reads_in_a_safe_order_by_natural_key(): async_fetch_reads_in_a_safe_order(new StoreShape(
    opts => opts.Projections.Snapshot<KeyedSimpleAggregate>(SnapshotLifecycle.Async),
    NaturalKeyStoreShapes.Keyed));

public class async_fetch_reads_in_a_safe_order_by_natural_key_on_string_streams(): async_fetch_reads_in_a_safe_order(
    new StoreShape(
        opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<KeyedSimpleAggregateAsString>(SnapshotLifecycle.Async);
        },
        NaturalKeyStoreShapes.KeyedAsString));

public class async_fetch_reads_in_a_safe_order_by_natural_key_with_conjoined_tenancy(): async_fetch_reads_in_a_safe_order(
    new StoreShape(
        opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.Snapshot<KeyedSimpleAggregate>(SnapshotLifecycle.Async);
        },
        NaturalKeyStoreShapes.Keyed, Tenant: "tenant-a", OtherTenant: "tenant-b", SnapshotIsTenanted: true));

public class async_fetch_reads_in_a_safe_order_by_natural_key_with_the_identity_map(): async_fetch_reads_in_a_safe_order(
    new StoreShape(
        opts =>
        {
            opts.Projections.Snapshot<KeyedSimpleAggregate>(SnapshotLifecycle.Async);
            opts.Events.UseIdentityMapForAggregates = true;
        },
        NaturalKeyStoreShapes.Keyed));

public class async_fetch_reads_in_a_safe_order_by_natural_key_with_the_write_cache(): async_fetch_reads_in_a_safe_order(
    new StoreShape(
        opts =>
        {
            opts.Projections.Snapshot<KeyedSimpleAggregate>(SnapshotLifecycle.Async);
            opts.Events.CacheAggregatesForWriting<KeyedSimpleAggregate>();
        },
        NaturalKeyStoreShapes.Keyed));

public class async_fetch_reads_in_a_safe_order_by_natural_key_with_a_strong_typed_id(): async_fetch_reads_in_a_safe_order(
    new StoreShape(
        opts =>
        {
            opts.RegisterValueType<StrongTypedAggregateId>();
            opts.Projections.Snapshot<KeyedStrongTypedAggregate>(SnapshotLifecycle.Async);
        },
        NaturalKeyStoreShapes.KeyedStrongTyped));

public class async_fetch_reads_in_a_safe_order_by_mapped_natural_key(): async_fetch_reads_in_a_safe_order(new StoreShape(
    opts =>
    {
        opts.Projections.Snapshot<RaceKeyOwner>(SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<MappedKeySimpleAggregate>(SnapshotLifecycle.Async);
    },
    NaturalKeyStoreShapes.Mapped));
