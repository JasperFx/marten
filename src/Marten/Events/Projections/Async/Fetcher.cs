using System;
using System.Threading;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Events.Projections.Async
{
    public interface IEventPageWorker
    {
        void Receive(EventPage page);
    }

    public interface IFetcher
    {
        void Start(IEventPageWorker worker);
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
        private Task _reset;

        public Fetcher(IStagedEventData eventData)
        {
            _eventData = eventData;
            _state = FetcherState.Waiting;
        }

        public void Start(IEventPageWorker worker)
        {
            _lock.Write(() =>
            {
                if (_state == FetcherState.Active) return;

                _fetchingTask = Task.Factory.StartNew(async () =>
                {
                    while (!_cancellation.IsCancellationRequested && _state == FetcherState.Active)
                    {
                        var page = await _eventData.FetchNextPage(_lastEncountered).ConfigureAwait(false);

                        if (page.Count == 0)
                        {
                            // TODO -- make the cooldown time be configurable
                            _reset = Task.Delay(1.Seconds(), _cancellation.Token).ContinueWith(t => Start(worker));
                            _state = FetcherState.Waiting;
                        }
                        else
                        {
                            worker.Receive(page);
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
        }
    }
}