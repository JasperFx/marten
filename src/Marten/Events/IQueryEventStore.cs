#nullable enable

using JasperFx.Events;
using Marten.Linq;

namespace Marten.Events;

/// <summary>
/// Marten's read-side event-store API.
/// </summary>
/// <remarks>
/// Marten 9 dedupe pillar: the database-agnostic surface now lives in
/// <see cref="JasperFx.Events.IQueryEventStore"/>. This interface adds the
/// Marten-specific LINQ-returning methods (<c>QueryRawEventDataOnly</c>,
/// <c>QueryAllRawEvents</c>) that return <see cref="IMartenQueryable{T}"/>.
/// </remarks>
public interface IQueryEventStore : JasperFx.Events.IQueryEventStore
{
    /// <summary>
    ///     Query directly against ONLY the raw event data. Use IQuerySession.Query() for aggregated documents!
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    IMartenQueryable<T> QueryRawEventDataOnly<T>() where T : class;

    /// <summary>
    ///     Query directly against the raw event data across all event types.
    /// </summary>
    /// <returns></returns>
    IMartenQueryable<IEvent> QueryAllRawEvents();
}
