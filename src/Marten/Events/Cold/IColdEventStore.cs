#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Marten.Events.Cold;

/// <summary>
///     Where the cold part of event streams lives: the events of a stream up to and including its cold
///     version (<c>mt_streams.cold_version</c>), after the application moved them out of <c>mt_events</c>.
///     Register it with <see cref="IEventStoreOptions.UseColdEventStore(IColdEventStore)" />.
/// </summary>
/// <remarks>
///     Marten only reads through this interface. Which streams go cold, when, and where their events are
///     kept is the application's business; <see cref="IEventStoreOperations.MarkStreamCold(string, long)" />
///     records the boundary. Implement the overload that matches the store's
///     <see cref="IReadOnlyEventStoreOptions.StreamIdentity" />. The session is the one doing the read, so it
///     carries the tenant (<see cref="IQuerySession.TenantId" />) and the database of the stream.
/// </remarks>
public interface IColdEventStore
{
    /// <summary>
    ///     The cold events of one stream, oldest first, at most one per version and none above
    ///     <paramref name="throughVersion" />.
    /// </summary>
    IAsyncEnumerable<ColdEvent> ReadAsync(IQuerySession session, string streamKey, long throughVersion,
        CancellationToken token)
        => throw new NotSupportedException(
            $"{GetType().Name} does not read string-keyed streams. Implement {nameof(IColdEventStore)}.{nameof(ReadAsync)}(IQuerySession, string, long, CancellationToken) when using StreamIdentity.AsString.");

    /// <inheritdoc cref="ReadAsync(IQuerySession, string, long, CancellationToken)" />
    IAsyncEnumerable<ColdEvent> ReadAsync(IQuerySession session, Guid streamId, long throughVersion,
        CancellationToken token)
        => throw new NotSupportedException(
            $"{GetType().Name} does not read Guid-identified streams. Implement {nameof(IColdEventStore)}.{nameof(ReadAsync)}(IQuerySession, Guid, long, CancellationToken) when using StreamIdentity.AsGuid.");
}
