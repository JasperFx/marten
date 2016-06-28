using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Events.Projections.Async
{

    public class CompleteRebuild : IEventPageWorker, IDisposable
    {
        private readonly IProjection _projection;
        private readonly IStagedEventData _eventData;
        private Fetcher _fetcher;
        private IDocumentSession _session;
        private ProjectionTrack _track;

        public CompleteRebuild(StagedEventOptions options, IDocumentStore store, IProjection projection)
        {
            

            _projection = projection;
            var storeOptions = store.Advanced.Options;
            var events = store.Schema.Events.As<EventGraph>();

            options.EventTypeNames = projection.Consumes.Select(x => events.EventMappingFor(x).Alias).ToArray();

            _eventData = new StagedEventData(options, storeOptions.ConnectionFactory(), events, storeOptions.Serializer());
            _fetcher = new Fetcher(_eventData, this);

            // TODO -- may want to be purging the identity map as you go
            _session = store.OpenSession();
            _track = new ProjectionTrack(events, projection, _session);
        }

        public async Task PerformRebuild(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        void IEventPageWorker.Receive(EventPage page)
        {
            throw new System.NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}