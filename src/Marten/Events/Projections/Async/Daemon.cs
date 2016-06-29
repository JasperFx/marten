using System;
using System.Collections.Generic;
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
        private readonly IList<IProjectionTrack> _projections = new List<IProjectionTrack>();
        private bool _isDisposed;


        public Daemon(DaemonOptions options, IFetcher fetcher, IEnumerable<IProjectionTrack> projections)
        {
            _options = options;
            _fetcher = fetcher;
            _projections.AddRange(projections);
        }

        public void Start()
        {
            UpdateBlock = new ActionBlock<IDaemonUpdate>(msg => msg.Invoke(this));
            _projections.Each(x => x.Updater = UpdateBlock);
            _fetcher.Start(this, true);
        }

        public ActionBlock<IDaemonUpdate> UpdateBlock { get; private set; }

        public async Task Stop()
        {
            await _fetcher.Stop().ConfigureAwait(false);

            foreach (var track in _projections)
            {
                await track.Stop().ConfigureAwait(false);
            }

        }

        public Accumulator Accumulator { get; } = new Accumulator();

        public async Task CachePage(EventPage page)
        {
            Accumulator.Store(page);

            if (Accumulator.CachedEventCount > _options.MaximumStagedEventCount)
            {
                await _fetcher.Pause().ConfigureAwait(false);
            }

            foreach (var track in _projections)
            {
                track.QueuePage(page);
            }
        }

        public Task StoreProgress(Type viewType, EventPage page)
        {
            var minimum = _projections.Min(x => x.LastEncountered);

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
            Dispose();


            
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            UpdateBlock.Complete();
            foreach (var track in _projections)
            {
                track.Dispose();
            }
        }

        public async Task<long> WaitUntilEventIsProcessed(long sequence)
        {
            long farthest = 0;
            foreach (var track in _projections)
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