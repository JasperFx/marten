using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Baseline;

namespace Marten.Events.Projections.Async
{
    public class Daemon : IEventPageWorker, IDisposable
    {
        private readonly DaemonOptions _options;
        private readonly IFetcher _fetcher;
        private readonly IActiveProjections _projections;


        public Daemon(DaemonOptions options, IFetcher fetcher, IActiveProjections projections)
        {
            _options = options;
            _fetcher = fetcher;
            _projections = projections;
        }

        public void Start()
        {
            UpdateBlock = new ActionBlock<IDaemonUpdate>(msg => msg.Invoke(this));
            _projections.StartTracks(UpdateBlock);
            _fetcher.Start(this, true);
        }

        public ActionBlock<IDaemonUpdate> UpdateBlock { get; private set; }

        public async Task Stop()
        {
            await _fetcher.Stop().ConfigureAwait(false);

            await _projections.StopAll().ConfigureAwait(false);
        }

        public Accumulator Accumulator { get; } = new Accumulator();

        public async Task CachePage(EventPage page)
        {
            Accumulator.Store(page);

            if (Accumulator.CachedEventCount > _options.MaximumStagedEventCount)
            {
                await _fetcher.Pause().ConfigureAwait(false);
            }

            _projections.CoordinatedTracks
                .Where(x => x.LastEncountered <= page.From)
                .Each(x => x.QueuePage(page));
        }

        public Task StoreProgress(Type viewType, EventPage page)
        {
            var minimum = _projections.CoordinatedTracks.Min(x => x.LastEncountered);

            Accumulator.Prune(minimum);

            if (Accumulator.CachedEventCount <= _options.MaximumStagedEventCount &&
                _fetcher.State == FetcherState.Paused)
            {
                _fetcher.Start(this, true);
            }

            return Task.CompletedTask;
        }

        void IEventPageWorker.QueuePage(EventPage page)
        {
            UpdateBlock.Post(new CachePageUpdate(page));
        }

        void IEventPageWorker.Finished(long lastEncountered)
        {
            // TODO -- do something here!
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            UpdateBlock.Complete();
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