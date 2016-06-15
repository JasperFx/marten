using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public interface IStagedEventData
    {
        Task<long> LastEventProgression();

        Task RegisterProgress(long lastEncountered);


        Task<EventPage> FetchNextPage(long lastEncountered);
    }
}