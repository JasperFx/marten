#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Events.Dcb;

namespace Marten.Events;

/// <summary>
/// Marten's combined session-level event-store API: read + write + aggregate-handler
/// workflow.
/// </summary>
/// <remarks>
/// Marten 9 dedupe pillar: the database-agnostic surface (FetchForWriting,
/// WriteToAggregate, AppendOptimistic/Exclusive, FetchLatest, ProjectLatest, tag
/// queries, natural-key fetches, etc.) now lives in
/// <see cref="JasperFx.Events.IEventStoreOperations"/>. This interface adds the
/// Marten-specific extras:
/// <list type="bullet">
///   <item><c>FetchForWritingByTags&lt;T&gt;</c> — returns a Marten-specific
///   <see cref="IEventBoundary{T}"/> for DCB workflows. Lifts to JFx.Events once
///   Polecat reaches DCB parity (JasperFx/polecat#80).</item>
///   <item><c>StreamLatestJson&lt;T&gt;</c> — Marten-specific JSON-passthrough
///   optimization that streams raw aggregate JSON to a destination stream.</item>
/// </list>
/// </remarks>
public interface IEventStoreOperations : JasperFx.Events.IEventStoreOperations, IEventOperations, IQueryEventStore
{
    /// <summary>
    /// Fetch events by tag query, aggregate into T, and establish a DCB consistency boundary.
    /// At SaveChangesAsync() time, Marten will assert no new matching events were added
    /// since the query was executed.
    /// </summary>
    Task<IEventBoundary<T>> FetchForWritingByTags<T>(EventTagQuery query,
        CancellationToken cancellation = default) where T : class;

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
}
