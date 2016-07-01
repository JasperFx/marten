using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public interface IFetcher
    {
        void Start(IProjectionTrack track, DaemonLifecycle lifecycle);
        Task Pause();
        Task Stop();
        FetcherState State { get; }
        Task<EventPage> FetchNextPage(long lastEncountered);
    }
}