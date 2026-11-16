#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;

namespace Marten.Events;

/// <summary>
/// Marten's combined session-level event-store API: read + write + aggregate-handler
/// workflow.
/// </summary>
/// <remarks>
/// Marten 9 dedupe pillar: the database-agnostic surface (FetchForWriting,
/// WriteToAggregate, AppendOptimistic/Exclusive, FetchLatest, ProjectLatest, tag
/// queries, natural-key fetches, plus <c>FetchForWritingByTags&lt;T&gt;</c>) now
/// lives in <see cref="JasperFx.Events.IEventStoreOperations"/>. This interface
/// inherits the canonical contract and adds the Marten-specific extras:
/// <list type="bullet">
///   <item><c>StreamLatestJson&lt;T&gt;</c> — Marten-specific JSON-passthrough
///   optimization that streams raw aggregate JSON to a destination stream.</item>
/// </list>
/// <para>
/// <c>UnArchiveStream(Guid)</c> / <c>UnArchiveStream(string)</c> are inherited from
/// <see cref="JasperFx.Events.IEventStoreOperations"/> (jasperfx#1186, JasperFx 3.3.0) rather than
/// redeclared here. Marten-specific behaviour: under <c>UseArchivedStreamPartitioning</c> the stream's
/// rows move back out of the archived partition, with their DCB tag and natural key rows, and an
/// un-archive is refused with <see cref="Marten.Exceptions.InvalidStreamOperationException"/> on
/// <c>SaveChangesAsync</c> when a new live stream has since reused the archived stream's identity.
/// Projections are not rewound.
/// </para>
/// The read-only tag queries are declared both on <see cref="JasperFx.Events.IEventStoreOperations"/>
/// and on Marten's <see cref="IQueryEventStore"/>, and are redeclared here so that a call through
/// this interface is not ambiguous between the two.
/// </remarks>
public interface IEventStoreOperations : JasperFx.Events.IEventStoreOperations, IEventOperations, IQueryEventStore
{
    /// <summary>
    ///     Stream the raw JSON of the projected aggregate T by id directly to a destination stream.
    /// This avoids any deserialization/serialization round-trip when the aggregate is stored inline or
    /// the async projection is caught up. Returns true if found, false if not found.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="destination"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns>True if the aggregate was found and written to the destination stream</returns>
    Task<bool> StreamLatestJson<T>(Guid id, Stream destination, CancellationToken cancellation = default) where T : class;

    /// <summary>
    ///     Stream the raw JSON of the projected aggregate T by id directly to a destination stream.
    /// This avoids any deserialization/serialization round-trip when the aggregate is stored inline or
    /// the async projection is caught up. Returns true if found, false if not found.
    /// </summary>
    Task<bool> StreamLatestJson<T>(string id, Stream destination, CancellationToken cancellation = default) where T : class;

    /// <inheritdoc cref="IQueryEventStore.EventsExistAsync"/>
    new Task<bool> EventsExistAsync(EventTagQuery query, CancellationToken cancellation = default);

    /// <inheritdoc cref="IQueryEventStore.QueryByTagsAsync"/>
    new Task<IReadOnlyList<IEvent>> QueryByTagsAsync(EventTagQuery query, CancellationToken cancellation = default);

    /// <inheritdoc cref="IQueryEventStore.AggregateByTagsAsync{T}"/>
    new Task<T?> AggregateByTagsAsync<T>(EventTagQuery query, CancellationToken cancellation = default) where T : class;

    /// <summary>
    ///     On <c>SaveChangesAsync</c>, delete the stream's events through <paramref name="throughVersion" /> from
    ///     <c>mt_events</c> and record that they now live in the configured cold event store. Copy them there
    ///     in the same unit of work. Requires <see cref="IEventStoreOptions.UseColdEventStore(Cold.IColdEventStore)" />.
    /// </summary>
    /// <remarks>
    ///     A stream keeps being appended to after it went cold, and is moved again with a higher
    ///     <paramref name="throughVersion" /> whenever the application decides; the boundary only moves up. A
    ///     version at or below the current boundary deletes nothing and changes nothing, so a retried move is
    ///     harmless.
    ///     <para>
    ///     Fails the unit of work, deleting nothing, when the stream does not exist or has not reached
    ///     <paramref name="throughVersion" />, or when a registered async projection or subscription has not
    ///     processed those events yet (its progression for this tenant is below the highest sequence being
    ///     deleted, or it has no progression yet). The async daemon never reads cold events, so moving them
    ///     earlier would silently lose them for that shard. Retry once the daemon caught up.
    ///     </para>
    /// </remarks>
    void MarkStreamCold(string streamKey, long throughVersion);

    /// <summary>
    ///     The cold version of a stream this unit of work appends to: null when the stream has no cold part or
    ///     is started by this unit of work. Usable from <see cref="IDocumentSessionListener.BeforeSaveChangesAsync" />
    ///     whichever way the events were appended. The first call resolves every appended stream this session has
    ///     not fetched yet in one query; a session that never asks pays nothing.
    /// </summary>
    /// <remarks>
    ///     Every appended stream whose cold version the session observed -- here, or through a live
    ///     <c>FetchForWriting</c> or <c>FetchLatest</c> -- is checked again when the unit of work commits, behind
    ///     the appends' row locks. If a concurrent <c>MarkStreamCold</c> changed it, the commit fails with
    ///     <see cref="Marten.Exceptions.ColdStreamChangedException" />.
    /// </remarks>
    ValueTask<long?> ColdVersionOfAsync(StreamAction stream, CancellationToken token = default);
}
