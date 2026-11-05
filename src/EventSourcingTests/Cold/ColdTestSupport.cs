using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events.Cold;
using Shouldly;

namespace EventSourcingTests.Cold;

/// <summary>
///     A cold event store that keeps events in memory, keyed by tenant and stream, the way an application
///     would keep them in its own tables.
/// </summary>
public class InMemoryColdEventStore: IColdEventStore
{
    private readonly ConcurrentDictionary<(string Tenant, string Stream), List<ColdEvent>> _streams = new();

    public int Reads;

    /// <summary>The throughVersion of every read, in order.</summary>
    public readonly ConcurrentQueue<long> ReadThrough = new();

    public long[] StoredVersions(string tenantId, string streamKey) =>
        _streams.TryGetValue((tenantId, streamKey), out var events)
            ? events.Select(x => x.Version).OrderBy(x => x).ToArray()
            : [];

    public void Store(string tenantId, string streamKey, IEnumerable<ColdEvent> events)
    {
        _streams.GetOrAdd((tenantId, streamKey), _ => new List<ColdEvent>()).AddRange(events);
    }

    public IAsyncEnumerable<ColdEvent> ReadAsync(IQuerySession session, string streamKey, long throughVersion,
        CancellationToken token) => read(session, streamKey, throughVersion, token);

    private async IAsyncEnumerable<ColdEvent> read(IQuerySession session, string stream, long throughVersion,
        [EnumeratorCancellation] CancellationToken token)
    {
        Interlocked.Increment(ref Reads);
        ReadThrough.Enqueue(throughVersion);

        // An application's store reads its own tables through the session, so the session has to be free.
        (await session.AdvancedSql.QueryAsync<int>("select 1", token)).Single().ShouldBe(1);

        var tenantId = session.TenantId;

        if (!_streams.TryGetValue((tenantId, stream), out var events)) yield break;

        foreach (var e in events.Where(x => x.Version <= throughVersion).OrderBy(x => x.Version))
        {
            yield return e;
        }
    }
}

public static class ColdTestSupport
{
    /// <summary>
    ///     What an application's move job does: copy the stream's events through <paramref name="throughVersion" />
    ///     to cold storage, and let Marten delete them from mt_events and record the boundary.
    /// </summary>
    public static async Task MoveToColdAsync(this IDocumentStore store, InMemoryColdEventStore cold, string tenantId,
        string streamKey, long throughVersion)
    {
        await using var session = store.LightweightSession(tenantId);
        var events = await session.Events.FetchStreamAsync(streamKey);

        cold.Store(tenantId, streamKey, events.Where(x => x.Version <= throughVersion).Select(x => x.ToColdEvent(store)));

        session.Events.MarkStreamCold(streamKey, throughVersion);
        await session.SaveChangesAsync();
    }

    public static ColdEvent ToColdEvent(this IEvent e, IDocumentStore store) => new()
    {
        Id = e.Id,
        Version = e.Version,
        Sequence = e.Sequence,
        EventTypeName = e.EventTypeName,
        DotNetTypeName = e.DotNetTypeName,
        Timestamp = e.Timestamp,
        Data = store.Options.Serializer().ToJson(e.Data),
        Headers = e.Headers,
        CorrelationId = e.CorrelationId,
        CausationId = e.CausationId,
        UserName = e.UserName
    };
}
