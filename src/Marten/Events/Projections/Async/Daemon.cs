using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Baseline;

namespace Marten.Events.Projections.Async
{
    public class Daemon : IEventPageWorker, IDisposable
    {
        private readonly Accumulator _accumulator = new Accumulator();
        private readonly IFetcher _fetcher;
        private readonly IActiveProjections _projections;
        private ActionBlock<IDaemonUpdate> _updateBlock;


        public Daemon(IFetcher fetcher, IActiveProjections projections)
        {
            _fetcher = fetcher;
            _projections = projections;
        }

        public void Start()
        {
            _updateBlock = new ActionBlock<IDaemonUpdate>(msg => msg.Invoke(this));
            _projections.StartTracks(_updateBlock);
            _fetcher.Start(this, true);
        }

        public async Task Stop()
        {
            await _fetcher.Stop().ConfigureAwait(false);

            await _projections.StopAll().ConfigureAwait(false);
        }


        public async Task CachePage(EventPage page)
        {
            _accumulator.Store(page);

            // TODO -- make the threshold be configurable
            if (_accumulator.CachedEventCount > 10000)
            {
                await _fetcher.Pause().ConfigureAwait(false);
            }

            _projections.CoordinatedTracks
                .Where(x => x.LastEncountered <= page.From)
                .Each(x => x.QueuePage(page));
        }

        public Task StoreProgress(Type viewType, EventPage page)
        {
            // TODO -- Need to update projection status
            // * Trim off anything that's obsolete
            // * if under the maximum items stored, make sure that the fetcher is started
            // * if there is any more cached after this view, send the next page on
            // * if there are downstream projections from this one, send the page on
            //   to the next projection

            throw new NotImplementedException();
        }

        void IEventPageWorker.QueuePage(EventPage page)
        {
            _updateBlock.Post(new CachePageUpdate(page));
        }

        void IEventPageWorker.Finished(long lastEncountered)
        {
            // TODO -- do something here!
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            _updateBlock.Complete();
            _projections.Dispose();
        }

        public async Task<long> WaitUntilEventIsProcessed(long sequence)
        {
            long farthest = 0;
            foreach (var track in _projections.AllTracks)
            {
                var last = await track.WaitUntilEventIsProcessed(sequence).ConfigureAwait(false);
                if (farthest == 0 || last < farthest)
                {
                    farthest = last;
                }
            }

            return farthest;
        }
    }
}