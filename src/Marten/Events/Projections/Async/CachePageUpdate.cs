using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public class CachePageUpdate : IDaemonUpdate
    {
        private readonly EventPage _page;

        public CachePageUpdate(EventPage page)
        {
            _page = page;
        }

        public Task Invoke(Daemon daemon)
        {
            return daemon.CachePage(_page);
        }
    }
}