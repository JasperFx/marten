using System;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public class StoreProgress: IDaemonUpdate
    {
        private readonly Type _viewType;
        private readonly EventPage _page;

        public StoreProgress(Type viewType, EventPage page)
        {
            _viewType = viewType;
            _page = page;
        }

        public Task Invoke(ProjectionTrack projectionTrack)
        {
            return projectionTrack.StoreProgress(_viewType, _page);
        }
    }
}
