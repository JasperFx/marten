using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public interface IFetcher
    {
        void Start(IProjectionTrack track, DaemonLifecycle lifecycle, CancellationToken token = default(CancellationToken));

        Task Pause();

        Task Stop();

        FetcherState State { get; }

        Task<EventPage> FetchNextPage(long lastEncountered);

        void Reset();
    }
}
