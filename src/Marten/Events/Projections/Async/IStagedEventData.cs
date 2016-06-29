using System;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public interface IStagedEventData: IDisposable
    {
        Task<EventPage> FetchNextPage(long lastEncountered);
    }
}