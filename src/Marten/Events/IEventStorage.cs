using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;

namespace Marten.Events
{
    public interface IEventStorage: ISelector<IEvent>, IDocumentStorage<IEvent>
    {
        EventGraph Events { get; }
        IStorageOperation AppendEvent(EventGraph events, IMartenSession session, StreamAction stream, IEvent e);
        IStorageOperation InsertStream(StreamAction stream);
        IQueryHandler<StreamState> QueryForStream(StreamAction stream);
        IStorageOperation UpdateStreamVersion(StreamAction stream);
    }
}
