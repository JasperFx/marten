#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;

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
    ///     On <c>SaveChangesAsync</c>, delete the stream's events through <paramref name="throughVersion" /> from
    ///     <c>mt_events</c> and record that they now live in the configured cold event store. Copy them there
    ///     in the same unit of work. Fails the unit of work when the stream does not exist or has not reached
    ///     <paramref name="throughVersion" />. Requires <see cref="IEventStoreOptions.UseColdEventStore(Cold.IColdEventStore)" />.
    /// </summary>
    void MarkStreamCold(Guid streamId, long throughVersion);

    /// <inheritdoc cref="MarkStreamCold(Guid, long)" />
    void MarkStreamCold(string streamKey, long throughVersion);

    /// <summary>
    ///     On <c>SaveChangesAsync</c>, forget the stream's cold part: after this the stream is read from
    ///     <c>mt_events</c> alone, so put its cold events back there first.
    /// </summary>
    void MarkStreamWarm(Guid streamId);

    /// <inheritdoc cref="MarkStreamWarm(Guid)" />
    void MarkStreamWarm(string streamKey);

    /// <summary>
    ///     The cold version of a stream this unit of work appends to, as stored before it: null when the stream
    ///     has no cold part or is started by this unit of work. Meant for <see cref="IDocumentSessionListener.BeforeSaveChangesAsync" />,
    ///     by which time Marten has resolved it for every appended stream, whichever way the events were appended.
    ///     Before that it is only known for streams fetched with <c>FetchForWriting</c> or <c>FetchLatest</c>.
    /// </summary>
    long? ColdVersionOf(StreamAction stream);
}
