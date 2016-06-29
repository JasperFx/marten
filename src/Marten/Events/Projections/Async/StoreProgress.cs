using System;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public class StoreProgress : IDaemonUpdate
    {
        private readonly Type _viewType;
        private readonly EventPage _page;

        public StoreProgress(Type viewType, EventPage page)
        {
            _viewType = viewType;
            _page = page;
        }

        public Task Invoke(Daemon daemon)
        {
            return daemon.StoreProgress(_viewType, _page);
        }
    }
}