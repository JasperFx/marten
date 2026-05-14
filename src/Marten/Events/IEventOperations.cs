using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Protected;

namespace Marten.Events;

/// <summary>
/// Marten's write-side event-store basics (Append, StartStream, CompactStream).
/// </summary>
/// <remarks>
/// Marten 9 dedupe pillar: the database-agnostic Append / StartStream surface now
/// lives in <see cref="JasperFx.Events.IEventOperations"/>. This interface adds the
/// Marten-specific <c>CompactStreamAsync&lt;T&gt;</c> overloads — their execution
/// depends on the lifted <see cref="StreamCompactingRequest{T}"/> data shape from
/// <c>JasperFx.Events.Protected</c> (jasperfx#269 / PR #274), but the execution
/// itself stays Marten-specific because it threads <c>DocumentSessionBase</c>.
/// </remarks>
public interface IEventOperations : JasperFx.Events.IEventOperations
{
    /// <summary>
    /// Compact a stream by replacing its first event with a Compacted&lt;T&gt; event that establishes
    /// the snapshot. Do this when you do not care about older stream data, but do want to
    /// keep the database size down for better performance.
    /// </summary>
    /// <param name="streamKey">The string identifier for the stream</param>
    /// <param name="configure">Configure the compacting request. Default is to compact at the latest point</param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task CompactStreamAsync<T>(string streamKey, Action<StreamCompactingRequest<T>>? configure = null) where T : class;

    /// <summary>
    /// Compact a stream by replacing its first event with a Compacted&lt;T&gt; event that establishes
    /// the snapshot.
    /// </summary>
    Task CompactStreamAsync<T>(Guid streamId, Action<StreamCompactingRequest<T>>? configure = null) where T : class;
}
