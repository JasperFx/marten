#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Linq;

namespace Marten.Events;

/// <summary>
/// Marten's read-side event-store API.
/// </summary>
/// <remarks>
/// Marten 9 dedupe pillar: the database-agnostic surface now lives in
/// <see cref="JasperFx.Events.IQueryEventStore"/>. This interface adds the
/// Marten-specific LINQ-returning methods (<c>QueryRawEventDataOnly</c>,
/// <c>QueryAllRawEvents</c>) that return <see cref="IMartenQueryable{T}"/>, and the read-only
/// Dynamic Consistency Boundary tag queries (<c>EventsExistAsync</c>, <c>QueryByTagsAsync</c>,
/// <c>AggregateByTagsAsync</c>), so a query session can read by tags as a document session can.
/// </remarks>
public interface IQueryEventStore : JasperFx.Events.IQueryEventStore
{
    /// <summary>
    ///     Query directly against ONLY the raw event data. Use IQuerySession.Query() for aggregated documents!
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    IMartenQueryable<T> QueryRawEventDataOnly<T>() where T : notnull;

    /// <summary>
    ///     Query directly against the raw event data across all event types.
    /// </summary>
    /// <returns></returns>
    IMartenQueryable<IEvent> QueryAllRawEvents();

    /// <summary>
    /// Lightweight existence check: are there any events matching the given tag query?
    /// Useful for DCB guard clauses.
    /// </summary>
    Task<bool> EventsExistAsync(EventTagQuery query, CancellationToken cancellation = default);

    /// <summary>
    /// Query events by their tags. Returns events matching any of the OR'd conditions
    /// in the query, ordered by sequence id.
    /// </summary>
    Task<IReadOnlyList<IEvent>> QueryByTagsAsync(EventTagQuery query, CancellationToken cancellation = default);

    /// <summary>
    /// Query events by tag and aggregate them into type T using a live fold.
    /// </summary>
    Task<T?> AggregateByTagsAsync<T>(EventTagQuery query, CancellationToken cancellation = default) where T : class;
}
