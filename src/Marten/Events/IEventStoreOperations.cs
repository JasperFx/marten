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
