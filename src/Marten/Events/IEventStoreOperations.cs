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
/// The read-only tag queries are declared both on <see cref="JasperFx.Events.IEventStoreOperations"/>
/// and on Marten's <see cref="IQueryEventStore"/>, and are redeclared here so that a call through
/// this interface is not ambiguous between the two.
/// </remarks>
public interface IEventStoreOperations : JasperFx.Events.IEventStoreOperations, IEventOperations, IQueryEventStore
{
    /// <summary>
    ///     Reverse <see cref="JasperFx.Events.IEventStoreOperations.ArchiveStream(Guid)"/>: the stream and all of
    ///     its events become live again, so they are read, aggregated and appended to as before. Queued until
    ///     <c>SaveChangesAsync</c>, like <c>ArchiveStream</c>.
    /// </summary>
    /// <param name="streamId">Identity of the stream to un-archive.</param>
    /// <remarks>
    ///     <para>
    ///         Only the stream in the session's tenant is touched. Un-archiving a stream that is not archived,
    ///         or that does not exist, does nothing and is not an error.
    ///     </para>
    ///     <para>
    ///         Under <c>UseArchivedStreamPartitioning</c> the stream's rows move back out of the archived
    ///         partition, together with their DCB tag rows and natural key lookup rows. That layout also lets a
    ///         new stream reuse an archived stream's identity; if that has happened, un-archiving the old
    ///         stream is refused with an <see cref="Marten.Exceptions.InvalidStreamOperationException"/> on
    ///         <c>SaveChangesAsync</c>, because two live streams cannot share one identity.
    ///     </para>
    ///     <para>
    ///         Projections are not rewound. A projected document that was deleted or left behind when the
    ///         stream was archived stays as it is until new events arrive or the projection is rebuilt.
    ///     </para>
    /// </remarks>
    void UnArchiveStream(Guid streamId);

    /// <summary>
    ///     Reverse <see cref="JasperFx.Events.IEventStoreOperations.ArchiveStream(string)"/>: the stream and all
    ///     of its events become live again. Queued until <c>SaveChangesAsync</c>, like <c>ArchiveStream</c>.
    /// </summary>
    /// <param name="streamKey">String identity of the stream to un-archive.</param>
    /// <inheritdoc cref="UnArchiveStream(Guid)" path="/remarks"/>
    void UnArchiveStream(string streamKey);

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
}
