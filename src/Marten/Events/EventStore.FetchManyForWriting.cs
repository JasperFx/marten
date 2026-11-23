#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Exceptions;

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
            id => Operations.FetchForWriting<T>(id, cancellation), cancellation);

    /// <inheritdoc cref="FetchManyForWriting{T}(IReadOnlyList{Guid}, CancellationToken)" />
    /// <exception cref="ArgumentException">When <paramref name="keys" /> repeats a key.</exception>
    public Task<IReadOnlyList<IEventStream<T>>> FetchManyForWriting<T>(IReadOnlyList<string> keys,
        CancellationToken cancellation = default) where T : class
        => fetchManyForWritingAsync<T, string>(keys, nameof(keys),
            (batch, key) => batch.Events.FetchForWriting<T>(key),
            key => Operations.FetchForWriting<T>(key, cancellation), cancellation);

    /// <summary>
    ///     The many-stream form of <see cref="FetchForWriting{T, TId}(TId, CancellationToken)" />: by a natural
    ///     key, a strong-typed identifier or the stream id itself, in one round trip. A natural key is resolved
    ///     inside the batch.
    /// </summary>
    /// <remarks>
    ///     An id that matches no stream comes back as a handle with a null aggregate at version 0, as a single
    ///     <c>FetchForWriting</c> by that id would: appending starts the stream.
    /// </remarks>
    /// <exception cref="ArgumentException">
    ///     When <paramref name="ids" /> repeats an id, or <typeparamref name="TId" /> is <c>Nullable&lt;T&gt;</c>.
    /// </exception>
    /// <exception cref="ArgumentNullException">When an id is null.</exception>
    public Task<IReadOnlyList<IEventStream<T>>> FetchManyForWriting<T, TId>(IReadOnlyList<TId> ids,
        CancellationToken cancellation = default) where T : class where TId : notnull
    {
        // Ahead of the empty-list shortcut, so a TKey? call is refused whatever it is given, as
        // FetchForWriting<T, TKey?> is
        if (Nullable.GetUnderlyingType(typeof(TId)) is { } underlying)
        {
            throw new ArgumentException(
                $"{typeof(T).Name} cannot be fetched by {typeof(TId).Name}. Fetch by {underlying.Name} instead, and drop the null ids before fetching.",
                nameof(ids));
        }

        ArgumentNullException.ThrowIfNull(ids, nameof(ids));
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] is null)
            {
                throw new ArgumentNullException(nameof(ids),
                    $"{nameof(FetchManyForWriting)} was given a null id at position {i}. Drop the null ids before fetching.");
            }
        }

        return fetchManyForWritingAsync<T, TId>(ids, nameof(ids),
            (batch, id) => batch.Events.FetchForWriting<T, TId>(id),
            id => FetchForWriting<T, TId>(id, cancellation), cancellation);
    }

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

        var batch = _session.CreateBatchQuery();

        // Each fetch in the batch starts or appends to its stream in this session as it is read (#6073)
        var streamActionsBefore = _session.WorkTracker.Streams.Count;

        // Enlisting is synchronous and hands back an unawaited Task per stream; nothing is executed
        // until Execute, which is what makes this one round trip instead of identities.Count.
        var pending = new Task<IEventStream<T>>[identities.Count];
        for (var i = 0; i < identities.Count; i++)
        {
            pending[i] = enlist(batch, identities[i]);
        }

        try
        {
            await batch.Execute(cancellation).ConfigureAwait(false);
        }
        catch (Exception e) when (e is SnapshotRaceException or NaturalKeyMovedException)
        {
            // An Async snapshot went backwards mid-batch, as it does while a projection is rebuilt, or a natural
            // key changed hands while it was read. A batch cannot redo one fetch, but the single-stream fetch
            // can, so fall back to one per stream. Nothing was handed out yet, so the abandoned batch items are never observed -- but the fetches
            // read before the refused one already added their stream actions to the session. Take those back,
            // or a new stream would be started twice: once by the batch and again by the fallback (#6073).
            discardStreamActionsSince(streamActionsBefore);

            var sequential = new IEventStream<T>[identities.Count];
            for (var i = 0; i < identities.Count; i++)
            {
                sequential[i] = await fetchOne(identities[i]).ConfigureAwait(false);
            }

            return sequential;
        }

        var streams = new IEventStream<T>[identities.Count];
        for (var i = 0; i < identities.Count; i++)
        {
            streams[i] = await pending[i].ConfigureAwait(false);
        }

        return streams;
    }

    /// <summary>
    ///     Removes the stream actions an abandoned batch added. Executing the batch only ever adds to the
    ///     session's streams, so everything past the count taken before it ran belongs to the batch.
    /// </summary>
    private void discardStreamActionsSince(int count)
    {
        var streams = _session.WorkTracker.Streams;
        if (streams.Count > count)
        {
            streams.RemoveRange(count, streams.Count - count);
        }
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
