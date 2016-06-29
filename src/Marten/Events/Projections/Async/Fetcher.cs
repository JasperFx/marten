using System;
using System.Threading;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Events.Projections.Async
{
    public interface IEventPageWorker
    {
        void QueuePage(EventPage page);
        void Finished(long lastEncountered);
    }

    public interface IFetcher
    {
        void Start(IEventPageWorker worker, bool waitForMoreOnEmpty);
        Task Pause();
        Task Stop();
        FetcherState State { get; }
    }

    public class Fetcher : IDisposable, IFetcher
    {
        private readonly IStagedEventData _eventData;
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private FetcherState _state;
        private Task _fetchingTask;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private long _lastEncountered = 0;

        public Fetcher(IStagedEventData eventData)
        {
            _eventData = eventData;
            _state = FetcherState.Waiting;
        }

        public void Start(IEventPageWorker worker, bool waitForMoreOnEmpty)
        {
            _lock.Write(() =>
            {
                if (_state == FetcherState.Active) return;

                _state = FetcherState.Active;

                _fetchingTask = Task.Factory.StartNew(async () =>
                {
                    while (!_cancellation.IsCancellationRequested && _state == FetcherState.Active)
                    {
                        var page = await _eventData.FetchNextPage(_lastEncountered).ConfigureAwait(false);

                        if (page.Count == 0)
                        {
                            if (waitForMoreOnEmpty)
                            {
                                _state = FetcherState.Waiting;
                                
                                // TODO -- make the cooldown time be configurable
                                await Task.Delay(1.Seconds(), _cancellation.Token).ConfigureAwait(false);
                                Start(worker, waitForMoreOnEmpty);
                            }
                            else
                            {
                                _state = FetcherState.Paused;
                                worker.Finished(_lastEncountered);
                                break;
                            }
                        }
                        else
                        {
                            _lastEncountered = page.To;
                            worker.QueuePage(page);
                        }
                    }
                }, _cancellation.Token);
            });
        }


        public async Task Pause()
        {
            _lock.EnterWriteLock();
            try
            {
                _state = FetcherState.Paused;

                await _fetchingTask.ConfigureAwait(false);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public async Task Stop()
        {
            _lock.EnterWriteLock();
            try
            {
                _state = FetcherState.Waiting;

                await _fetchingTask.ConfigureAwait(false);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public FetcherState State
        {
            get { return _lock.Read(() => _state); }
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            _eventData.Dispose();
        }
    }
}