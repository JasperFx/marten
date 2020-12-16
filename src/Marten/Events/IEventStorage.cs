using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;

namespace Marten.Events
{
    /// <summary>
    /// The implementation of this class is generated at runtime based on the configuration
    /// of the system
    /// </summary>
    public interface IEventStorage: ISelector<IEvent>, IDocumentStorage<IEvent>
    {
        /// <summary>
        /// Create a storage operation to append a single event
        /// </summary>
        /// <param name="events"></param>
        /// <param name="session"></param>
        /// <param name="stream"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        IStorageOperation AppendEvent(EventGraph events, IMartenSession session, StreamAction stream, IEvent e);

        /// <summary>
        /// Create a storage operation to insert a single event stream record
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        IStorageOperation InsertStream(StreamAction stream);

        /// <summary>
        /// Create an IQueryHandler to find and load a Stream
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        IQueryHandler<StreamState> QueryForStream(StreamAction stream);

        /// <summary>
        /// Create a storage operation for updating the version of a single stream
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        IStorageOperation UpdateStreamVersion(StreamAction stream);
    }
}
