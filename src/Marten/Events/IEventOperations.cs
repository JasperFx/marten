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
    // CompactStreamAsync<T> used to be declared here. jasperfx#635 / marten#5153 lifted the two
    // overloads onto JasperFx.Events.IEventStoreOperations, which Marten.Events.IEventStoreOperations
    // already inherits alongside this interface -- so keeping the local copy made every call site
    // ambiguous (CS0121). The implementation is unchanged and still lives in
    // Events/EventStore.StreamCompacting.cs; only the declaration moved.
}
