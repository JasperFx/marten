#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;

namespace Marten.Events.Cold;

/// <summary>
///     Cold event storage, opted into with <c>using Marten.Events.Cold;</c>: streams whose oldest events the
///     application moved out of <c>mt_events</c> and keeps itself.
/// </summary>
/// <remarks>
///     Experimental: the shape may still change. Rebuilds, projections registered after streams went cold and
///     stream compaction do not read the cold part; see the documentation.
/// </remarks>
[Experimental(ColdEventStorage.DiagnosticId, UrlFormat = ColdEventStorage.DocumentationUrl)]
public static class ColdEventStorage
{
    public const string DiagnosticId = "MARTEN001";
    internal const string DocumentationUrl = "https://martendb.io/events/cold-storage.html";

    /// <summary>
    ///     Let streams have a cold part: their events up to <c>mt_streams.cold_version</c>, moved out of
    ///     <c>mt_events</c> by the application and read back through <paramref name="coldEventStore" />.
    ///     Live aggregations (<c>FetchForWriting</c>, <c>FetchLatest</c>, <c>AggregateStreamAsync</c>) fold the
    ///     cold events first. Requires string stream identity. Adds the nullable <c>cold_version</c> column to
    ///     <c>mt_streams</c>.
    /// </summary>
    public static IEventStoreOptions UseColdEventStore(this IEventStoreOptions options, IColdEventStore coldEventStore)
    {
        ArgumentNullException.ThrowIfNull(coldEventStore);
        eventGraphOf(options).ColdEventStore = coldEventStore;
        return options;
    }

    /// <inheritdoc cref="UseColdEventStore(IEventStoreOptions, IColdEventStore)" />
    public static IEventStoreOptions UseColdEventStore<T>(this IEventStoreOptions options)
        where T : IColdEventStore, new()
    {
        return options.UseColdEventStore(new T());
    }

    /// <summary>
    ///     On <c>SaveChangesAsync</c>, delete the stream's events through <paramref name="throughVersion" /> from
    ///     <c>mt_events</c> and record that they now live in the configured cold event store.
    /// </summary>
    /// <remarks>
    ///     A cold store in the same PostgreSQL database can be written in the same unit of work. A cold store
    ///     anywhere else is written first, and this is called once that copy is durable; the copy has to tolerate
    ///     being made twice, because a failed or lagging move is retried.
    ///     <para>
    ///     A stream keeps being appended to after it went cold, and is moved again with a higher
    ///     <paramref name="throughVersion" /> whenever the application decides; the boundary only moves up. A
    ///     version at or below the current boundary deletes nothing and changes nothing, so a retried move is
    ///     harmless.
    ///     </para>
    ///     <para>
    ///     Fails the unit of work, deleting nothing, when the stream does not exist or has not reached
    ///     <paramref name="throughVersion" />, or when a registered async projection or subscription has not
    ///     processed those events yet (its progression for this tenant is below the highest sequence being
    ///     deleted, or it has no progression yet). The async daemon never reads cold events, so moving them
    ///     earlier would silently lose them for that shard. Retry once the daemon caught up.
    ///     </para>
    /// </remarks>
    public static void MarkStreamCold(this IEventStoreOperations events, string streamKey, long throughVersion)
    {
        eventStoreOf(events).MarkStreamCold(streamKey, throughVersion);
    }

    /// <summary>
    ///     The cold version of a stream: null when it has no cold part or is started by this unit of work.
    ///     Usable from <see cref="IDocumentSessionListener.BeforeSaveChangesAsync" /> whichever way the events
    ///     were appended. The first call resolves every appended stream this unit of work has not fetched yet in
    ///     one query; a session that never asks pays nothing.
    /// </summary>
    /// <remarks>
    ///     Every appended stream whose cold version the unit of work observed -- here, or through a live
    ///     <c>FetchForWriting</c> or <c>FetchLatest</c> -- is checked again when it commits, behind the appends'
    ///     row locks. If a concurrent <c>MarkStreamCold</c> changed it, the commit fails with
    ///     <see cref="Marten.Exceptions.ColdStreamChangedException" />.
    /// </remarks>
    public static ValueTask<long?> ColdVersionOfAsync(this IEventStoreOperations events, StreamAction stream,
        CancellationToken token = default)
    {
        return eventStoreOf(events).ColdVersionOfAsync(stream, token);
    }

    private static EventGraph eventGraphOf(IEventStoreOptions options) =>
        options as EventGraph ?? throw new NotSupportedException(
            $"Cold event storage is configured on Marten's own event store options, not on {options.GetType().FullName}.");

    private static EventStore eventStoreOf(IEventStoreOperations events) =>
        events as EventStore ?? throw new NotSupportedException(
            $"Cold event storage works on a Marten session's own event store, not on {events.GetType().FullName}.");
}
