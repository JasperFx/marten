using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;

namespace Marten.Events;

/// <summary>
///     The implementation of this class is generated at runtime based on the configuration
///     of the system
/// </summary>
public interface IEventStorage: ISelector<IEvent>, ISelector<StreamState>, IDocumentStorage<IEvent>
{
    /// <summary>
    ///     The shared SELECT clause used to read <see cref="StreamState"/> rows from
    ///     <c>mt_streams</c>. The column order is locked to the order
    ///     <see cref="ISelector{StreamState}"/> reads from the <see cref="System.Data.Common.DbDataReader"/>.
    ///     Callers append a <c>WHERE</c> / <c>ORDER BY</c> / <c>LIMIT</c> clause for their own query
    ///     and pass the resulting reader rows back through <see cref="ISelector{T}.Resolve"/> /
    ///     <see cref="ISelector{T}.ResolveAsync"/>.
    /// </summary>
    string StreamStateSelectSql { get; }

    /// <summary>
    ///     Create a storage operation to append a single event
    /// </summary>
    /// <param name="events"></param>
    /// <param name="session"></param>
    /// <param name="stream"></param>
    /// <param name="e"></param>
    /// <returns></returns>
    // #4821 event E3: the closed-shape ops these return now live in Weasel.Storage and implement
    // only the neutral Weasel.Storage.IStorageOperation (a Marten IStorageOperation is-a neutral one,
    // so the legacy path still fits, and the session's QueueOperation already accepts neutral).
    Weasel.Storage.IStorageOperation AppendEvent(EventGraph events, IStorageSession session, StreamAction stream, IEvent e);

    /// <summary>
    ///     Create a storage operation to insert a single event stream record
    /// </summary>
    /// <param name="stream"></param>
    /// <returns></returns>
    Weasel.Storage.IStorageOperation InsertStream(StreamAction stream);

    /// <summary>
    ///     Create an IQueryHandler to find and load a Stream
    /// </summary>
    /// <param name="stream"></param>
    /// <returns></returns>
    IQueryHandler<StreamState> QueryForStream(StreamAction stream);

    /// <summary>
    ///     Create a storage operation for updating the version of a single stream
    /// </summary>
    /// <param name="stream"></param>
    /// <returns></returns>
    Weasel.Storage.IStorageOperation UpdateStreamVersion(StreamAction stream);

    /// <summary>
    ///     Create a storage operation that asserts a stream's expected version
    ///     without appending events (the <c>AlwaysEnforceConsistency</c>
    ///     zero-events path).
    /// </summary>
    /// <param name="stream"></param>
    /// <returns></returns>
    Weasel.Storage.IStorageOperation AssertStreamVersion(StreamAction stream);

    /// <summary>
    /// Create a storage operation to just increment the existing stream
    /// based on the number of the events being appended
    /// </summary>
    /// <param name="stream"></param>
    /// <returns></returns>
    IStorageOperation IncrementStreamVersion(StreamAction stream);

    Weasel.Storage.IStorageOperation QuickAppendEvents(StreamAction stream);

    /// <summary>
    /// #4968: create a storage operation to archive (soft-delete) a single stream and its events,
    /// routed through the shared Weasel.Storage event-store auxiliary-operation seam.
    /// </summary>
    Weasel.Storage.IStorageOperation ArchiveStream(object streamId, string tenantId);

    Weasel.Storage.IStorageOperation QuickAppendEventWithVersion(StreamAction stream,
        IEvent e);
}
