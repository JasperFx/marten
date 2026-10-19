#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    IMartenQueryable<T> QueryRawEventDataOnly<T>() where T : notnull;

    /// <summary>
    ///     Query directly against the raw event data across all event types.
    /// </summary>
    /// <returns></returns>
    IMartenQueryable<IEvent> QueryAllRawEvents();

    /// <summary>
    ///     Like <c>FetchStreamAsync</c>, but starting with the stream's cold part when it has one: the events
    ///     through <c>mt_streams.cold_version</c>, read from the configured cold event store, then the events in
    ///     <c>mt_events</c>. The version and timestamp filters apply to both.
    /// </summary>
    Task<IReadOnlyList<IEvent>> FetchStreamIncludingColdAsync(Guid streamId, long version = 0,
        DateTimeOffset? timestamp = null, long fromVersion = 0, CancellationToken token = default);

    /// <inheritdoc cref="FetchStreamIncludingColdAsync(Guid, long, DateTimeOffset?, long, CancellationToken)" />
    Task<IReadOnlyList<IEvent>> FetchStreamIncludingColdAsync(string streamKey, long version = 0,
        DateTimeOffset? timestamp = null, long fromVersion = 0, CancellationToken token = default);
}
