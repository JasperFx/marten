using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Marten.Events.Projections.Async
{
    public class Daemon : IEventPageWorker, IDisposable
    {
        private readonly IFetcher _fetcher;
        private readonly DaemonOptions _options;
        private readonly IProjectionTrack _projection;
        private bool _isDisposed;


        public Daemon(DaemonOptions options, IFetcher fetcher, IProjectionTrack projection)
        {
            _options = options;
            _fetcher = fetcher;
            _projection = projection;
        }

        public ActionBlock<IDaemonUpdate> UpdateBlock { get; private set; }

        public Accumulator Accumulator { get; } = new Accumulator();

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            UpdateBlock.Complete();
            _projection.Dispose();
        }

        void IEventPageWorker.QueuePage(EventPage page)
        {
            UpdateBlock.Post(new CachePageUpdate(page));
        }

        void IEventPageWorker.Finished(long lastEncountered)
        {
            Dispose();
        }

        public void Start()
        {
            UpdateBlock = new ActionBlock<IDaemonUpdate>(msg => msg.Invoke(this));
            _projection.Updater = UpdateBlock;
            _fetcher.Start(this, true);
        }

        public async Task Stop()
        {
            await _fetcher.Stop().ConfigureAwait(false);

            await _projection.Stop().ConfigureAwait(false);
        }

        public async Task CachePage(EventPage page)
        {
            Accumulator.Store(page);

            if (Accumulator.CachedEventCount > _options.MaximumStagedEventCount)
            {
                await _fetcher.Pause().ConfigureAwait(false);
            }

            _projection.QueuePage(page);
        }

        public Task StoreProgress(Type viewType, EventPage page)
        {
            var minimum = _projection.LastEncountered;

            Accumulator.Prune(minimum);

            if (Accumulator.CachedEventCount <= _options.MaximumStagedEventCount &&
                _fetcher.State == FetcherState.Paused)
            {
                _fetcher.Start(this, true);
            }

            return Task.CompletedTask;
        }

        public async Task<long> WaitUntilEventIsProcessed(long sequence)
        {
            long farthest = 0;

            var last = await _projection.WaitUntilEventIsProcessed(sequence).ConfigureAwait(false);
            if (farthest == 0 || last < farthest)
            {
                farthest = last;
            }

            return farthest;
        }
    }
}