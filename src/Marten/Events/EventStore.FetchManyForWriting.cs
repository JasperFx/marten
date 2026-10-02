#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;

namespace Marten.Events;

internal partial class EventStore
{
    /// <summary>
    ///     The many-stream form of <see cref="FetchForWriting{T}(Guid, CancellationToken)" />, in one round
    ///     trip rather than one per id.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     jasperfx#930 ships this default-implemented, and the default is correct but sequential: it
    ///     awaits <c>FetchForWriting</c> once per id, because a session is not safe for concurrent use.
    ///     Marten does not need that, because its batch query already enlists a <c>FetchForWriting</c>
    ///     per stream and executes the lot as one command (#5550).
    ///     </para>
    ///     <para>
    ///     The handles come back in the order asked for, so a caller can zip the result against its ids,
    ///     and each keeps its own starting version — <c>SaveChangesAsync</c> still guards every stream it
    ///     was appended to and no other. A stream that does not exist yet comes back as a handle with a
    ///     null aggregate at version 0, never as a gap.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentException">When <paramref name="ids" /> repeats an id.</exception>
    public Task<IReadOnlyList<IEventStream<T>>> FetchManyForWriting<T>(IReadOnlyList<Guid> ids,
        CancellationToken cancellation = default) where T : class
        => fetchManyForWritingAsync<T, Guid>(ids, nameof(ids), (batch, id) => batch.Events.FetchForWriting<T>(id),
            id => FetchForWriting<T>(id, cancellation), cancellation);

    /// <inheritdoc cref="FetchManyForWriting{T}(IReadOnlyList{Guid}, CancellationToken)" />
    /// <exception cref="ArgumentException">When <paramref name="keys" /> repeats a key.</exception>
    public Task<IReadOnlyList<IEventStream<T>>> FetchManyForWriting<T>(IReadOnlyList<string> keys,
        CancellationToken cancellation = default) where T : class
        => fetchManyForWritingAsync<T, string>(keys, nameof(keys),
            (batch, key) => batch.Events.FetchForWriting<T>(key),
            key => FetchForWriting<T>(key, cancellation), cancellation);

    private async Task<IReadOnlyList<IEventStream<T>>> fetchManyForWritingAsync<T, TId>(
        IReadOnlyList<TId> identities,
        string parameterName,
        Func<Services.BatchQuerying.IBatchedQuery, TId, Task<IEventStream<T>>> enlist,
        Func<TId, Task<IEventStream<T>>> fetchOne,
        CancellationToken cancellation) where T : class where TId : notnull
    {
        assertDistinct(identities, parameterName);

        if (identities.Count == 0)
        {
            return Array.Empty<IEventStream<T>>();
        }

        // An Async-projected aggregate cannot be batched more than once. Its fetch brackets its reads
        // in `begin transaction isolation level repeatable read read only` so they share one snapshot,
        // and PostgreSQL only accepts that as the FIRST statement in a transaction -- so the second
        // one in a batch is refused outright. Falling back to the sequential shape keeps this working
        // for that lifecycle, which is what the JasperFx default did and what callers already rely on;
        // refusing instead would make the faster path a regression for anyone using Async snapshots.
        if (FindFetchPlan<T, TId>().Lifecycle == ProjectionLifecycle.Async)
        {
            var sequential = new IEventStream<T>[identities.Count];
            for (var i = 0; i < identities.Count; i++)
            {
                sequential[i] = await fetchOne(identities[i]).ConfigureAwait(false);
            }

            return sequential;
        }

        var batch = _session.CreateBatchQuery();

        // Enlisting is synchronous and hands back an unawaited Task per stream; nothing is executed
        // until Execute, which is what makes this one round trip instead of identities.Count.
        var pending = new Task<IEventStream<T>>[identities.Count];
        for (var i = 0; i < identities.Count; i++)
        {
            pending[i] = enlist(batch, identities[i]);
        }

        await batch.Execute(cancellation).ConfigureAwait(false);

        var streams = new IEventStream<T>[identities.Count];
        for (var i = 0; i < identities.Count; i++)
        {
            streams[i] = await pending[i].ConfigureAwait(false);
        }

        return streams;
    }

    /// <summary>
    ///     Two handles on one stream in one session would race each other's expected version, so a
    ///     repeated identity is refused rather than quietly returning two views of the same stream.
    /// </summary>
    /// <remarks>
    ///     JasperFx's own <c>assertDistinct</c> is a private static on the interface, so it cannot be
    ///     reused from here; the contract is re-stated rather than inherited, and the compliance suite
    ///     is what keeps the two honest.
    /// </remarks>
    private static void assertDistinct<TId>(IReadOnlyList<TId> identities, string parameterName) where TId : notnull
    {
        ArgumentNullException.ThrowIfNull(identities, parameterName);

        var seen = new HashSet<TId>();
        foreach (var identity in identities)
        {
            if (!seen.Add(identity))
            {
                throw new ArgumentException(
                    $"{nameof(FetchManyForWriting)} was given the stream identity '{identity}' more than once. Each stream can be fetched for writing once per call, because two handles on one stream would race each other's expected version.",
                    parameterName);
            }
        }
    }
}
