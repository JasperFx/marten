using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public interface IFetcher
    {
        void Start(IEventPageWorker worker, DaemonLifecycle lifecycle);
        Task Pause();
        Task Stop();
        FetcherState State { get; }
    }
}