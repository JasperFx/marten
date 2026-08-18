#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Internal;
using Marten.Services;
using JasperFx.Events.Fetching;

namespace Marten.Events.Fetching;

/// <summary>
///     Aggregates fetched through a caching <c>FetchForWriting</c> in one session, waiting to be written
///     back to the cache once the session commits. Lives in the session's item map, so it is per session
///     and disappears with it.
/// </summary>
/// <remarks>
///     This is the piece that makes caching safe under the <b>Inline</b> lifecycle. Under Async, the
///     aggregate handed to the caller cannot drift ahead of the database — the daemon applies appended
///     events later — so <see cref="FetchAsyncPlan{TDoc,TId}" /> can write to the cache the moment it has
///     folded the delta. Under Inline that is a silent poisoning: the inline projection applies the
///     caller's new events to <i>this very instance</i> during commit, so an entry stored at fetch time
///     would be left describing state that is only durable if the commit succeeds.
///
///     Deferring the store to <see cref="IChangeListener.AfterCommitAsync" /> removes the hazard entirely
///     rather than mitigating it. Combined with take-on-read (<see cref="IAggregateWriteCache.TryTake" />,
///     which removes the entry as it hands it out) the failure path needs no compensation at all: a
///     rolled-back commit simply leaves no entry, and the next fetch misses and reloads from the database.
/// </remarks>
internal sealed class PendingAggregateCacheWrites
{
    private readonly List<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries => _entries;

    public void Track(IAggregateWriteCache cache, AggregateCacheKey key, object aggregate, object streamId,
        long fetchedVersion)
    {
        _entries.Add(new Entry(cache, key, aggregate, streamId, fetchedVersion));
    }

    internal sealed record Entry(
        IAggregateWriteCache Cache,
        AggregateCacheKey Key,
        object Aggregate,
        object StreamId,
        long FetchedVersion);
}

/// <summary>
///     Writes aggregates back to the <see cref="IAggregateWriteCache" /> after a successful commit, at the
///     stream version the commit actually landed on. Registered automatically the first time any aggregate
///     type opts into caching.
/// </summary>
internal sealed class AggregateWriteCacheListener: DocumentSessionListenerBase
{
    public static readonly AggregateWriteCacheListener Instance = new();

    public override Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        if (session is not IMartenSession martenSession) return Task.CompletedTask;

        if (!martenSession.ItemMap.TryGetValue(typeof(PendingAggregateCacheWrites), out var raw) ||
            raw is not PendingAggregateCacheWrites pending || pending.Entries.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Only remove after a *successful* commit. If SaveChangesAsync threw, this listener never runs, the
        // pending entries die with the session, and the cache is simply left without an entry -- which is
        // the correct state, because take-on-read already removed the stale one at fetch time.
        martenSession.ItemMap.Remove(typeof(PendingAggregateCacheWrites));

        var streams = commit.GetStreams().ToArray();

        foreach (var entry in pending.Entries)
        {
            // The committed StreamAction carries the version the stream ended up at. When this session
            // appended nothing to the stream there is no StreamAction, and the aggregate is still exactly
            // what was fetched -- so fall back to the fetched version rather than skipping the write back.
            var version = versionFor(streams, entry.StreamId) ?? entry.FetchedVersion;

            if (version > 0)
            {
                entry.Cache.Store(entry.Key, entry.Aggregate, version);
            }
        }

        return Task.CompletedTask;
    }

    private static long? versionFor(StreamAction[] streams, object streamId)
    {
        foreach (var stream in streams)
        {
            // Marten streams are identified by Guid or by string depending on StreamIdentity, and the
            // unused slot is left at its default -- so match on whichever one this aggregate was fetched by
            // rather than assuming an identity style.
            if (streamId is Guid guid)
            {
                if (stream.Id == guid) return stream.Version;
            }
            else if (streamId is string key)
            {
                if (stream.Key == key) return stream.Version;
            }
        }

        return null;
    }
}
