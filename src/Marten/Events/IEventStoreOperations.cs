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
///   <item><c>ProjectLatestForCommit&lt;T&gt;</c> — assigns pending events the metadata Marten's own commit
///   assigns, so it depends on how that commit prepares them.</item>
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

    /// <summary>
    ///     Project the aggregate T for the stream, including the events pending in this session, exactly as
    ///     <c>SaveChangesAsync()</c> will write it: the pending events are given now the versions, timestamps,
    ///     user name, correlation id and causation id that the commit then keeps. Nothing is stored.
    ///     Behaves as <c>ProjectLatest</c> when the stream has no pending events.
    /// </summary>
    /// <remarks>
    ///     Throws <see cref="InvalidOperationException"/> under <see cref="EventAppendMode.Quick"/>, and when the
    ///     stream's version is not known before commit: start it with <c>StartStream</c>, fetch it with
    ///     <c>FetchForWriting</c>, or append with an expected version. Takes the stream's id, and throws
    ///     <see cref="InvalidOperationException"/> for an aggregate's natural key. Under
    ///     <see cref="EventAppendMode.Rich"/> it can also throw the version conflict the commit would, after assigning
    ///     the pending events their versions.
    ///     The event sequence is still assigned at commit, and events appended after the call are not covered by it.
    /// </remarks>
    ValueTask<T?> ProjectLatestForCommit<T>(Guid id, CancellationToken cancellation = default) where T : class;

    /// <inheritdoc cref="ProjectLatestForCommit{T}(Guid, CancellationToken)"/>
    ValueTask<T?> ProjectLatestForCommit<T>(string id, CancellationToken cancellation = default) where T : class;

    /// <inheritdoc cref="IQueryEventStore.EventsExistAsync"/>
    new Task<bool> EventsExistAsync(EventTagQuery query, CancellationToken cancellation = default);

    /// <inheritdoc cref="IQueryEventStore.QueryByTagsAsync"/>
    new Task<IReadOnlyList<IEvent>> QueryByTagsAsync(EventTagQuery query, CancellationToken cancellation = default);

    /// <inheritdoc cref="IQueryEventStore.AggregateByTagsAsync{T}"/>
    new Task<T?> AggregateByTagsAsync<T>(EventTagQuery query, CancellationToken cancellation = default) where T : class;
}
