using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public interface IFetcher
    {
        void Start(IEventPageWorker worker, bool waitForMoreOnEmpty);
        Task Pause();
        Task Stop();
        FetcherState State { get; }
    }
}