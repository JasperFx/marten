using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public class CompleteRebuild : IEventPageWorker
    {
        private readonly IProjection _projection;
        private readonly IStagedEventData _eventData;
        private Fetcher _fetcher;

        public CompleteRebuild(IProjection projection, IStagedEventData eventData)
        {
            _projection = projection;
            _eventData = eventData;

            _fetcher = new Fetcher(_eventData, this);
        }

        public async Task PerformRebuild(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        void IEventPageWorker.Receive(EventPage page)
        {
            throw new System.NotImplementedException();
        }
    }
}