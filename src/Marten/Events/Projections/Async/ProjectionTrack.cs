using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Baseline;
using Marten.Util;

namespace Marten.Events.Projections.Async
{
    public class ProjectionTrack : IProjectionTrack
    {
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly EventGraph _events;
        private ActionBlock<EventPage> _executionTrack;
        private readonly IFetcher _fetcher;
        private readonly IDaemonLogger _logger;
        private readonly IProjection _projection;
        private readonly TaskCompletionSource<long> _rebuildCompletion = new TaskCompletionSource<long>();
        private readonly IDocumentStore _store;
        private readonly IList<EventWaiter> _waiters = new List<EventWaiter>();
        private bool _isDisposed;
        private bool _atEndOfEventLog;

        public ProjectionTrack(IFetcher fetcher, IDocumentStore store, IProjection projection, IDaemonLogger logger)
        {
            _fetcher = fetcher;
            _projection = projection;
            _logger = logger;
            _store = store;

            _events = store.Schema.Events;

            ViewType = _projection.Produces;
        }

        private void startConsumers()
        {
            _executionTrack = new ActionBlock<EventPage>(page => ExecutePage(page, _cancellation.Token),
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = 1,
                    EnsureOrdered = true
                });

            UpdateBlock = new ActionBlock<IDaemonUpdate>(msg => msg.Invoke(this), new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });
        }

        public ActionBlock<IDaemonUpdate> UpdateBlock { get; private set; }

        public Accumulator Accumulator { get; } = new Accumulator();

        public DaemonLifecycle Lifecycle { get; private set; } = DaemonLifecycle.Continuous;

        public Type ViewType { get; }

        public long LastEncountered { get; set; }

        public void Dispose()
        {
            if (_isDisposed) return;

            _cancellation.Cancel();

            _isDisposed = true;
            stopConsumers();

            _waiters.Clear();
            
        }

        private void stopConsumers()
        {
            UpdateBlock.Complete();
            _executionTrack.Complete();
        }

        public void QueuePage(EventPage page)
        {
            UpdateBlock.Post(new CachePageUpdate(page));
        }

        public void Finished(long lastEncountered)
        {
            _logger.FetchingFinished(this, lastEncountered);
            _atEndOfEventLog = true;

            if (_executionTrack.InputCount == 0)
            {
                _rebuildCompletion.SetResult(lastEncountered);
            }
            else
            {
                WaitUntilEventIsProcessed(lastEncountered).ContinueWith(t =>
                {
                    _rebuildCompletion.SetResult(t.Result);
                });
            }


        }

        public void Start(DaemonLifecycle lifecycle)
        {
            _logger.StartingProjection(this, lifecycle);

            _store.Schema.EnsureStorageExists(_projection.Produces);

            startConsumers();

            Lifecycle = lifecycle;
            _fetcher.Start(this, lifecycle, _cancellation.Token);


        }

        public async Task Stop()
        {
            _logger.Stopping(this);
            stopConsumers();
            await _fetcher.Stop().ConfigureAwait(false);
            _logger.Stopped(this);

            _rebuildCompletion.TrySetResult(LastEncountered);
        }

        public Task Start()
        {
            Start(Lifecycle);
            return Task.CompletedTask;
        }

        public Task<long> WaitUntilEventIsProcessed(long sequence)
        {
            if (LastEncountered >= sequence) return Task.FromResult(sequence);

            var waiter = new EventWaiter(sequence);
            _waiters.Add(waiter);

            return waiter.Completion.Task;
        }

        public Task<long> RunUntilEndOfEvents(CancellationToken token = default(CancellationToken))
        {
            _store.Schema.EnsureStorageExists(_projection.Produces);

            Start(DaemonLifecycle.StopAtEndOfEventData);

            return _rebuildCompletion.Task;
        }

        public async Task Rebuild(CancellationToken token = new CancellationToken())
        {
            Lifecycle = DaemonLifecycle.StopAtEndOfEventData;

            _store.Schema.EnsureStorageExists(_projection.Produces);

            await _fetcher.Stop().ConfigureAwait(false);

            await clearExistingState(token).ConfigureAwait(false);

            await RunUntilEndOfEvents(token).ConfigureAwait(false);
        }

        public async Task ExecutePage(EventPage page, CancellationToken cancellation)
        {
            // Duplicated, ignore. Shouldn't happen, but Fetcher is screwed up, so...
            if (page.To <= LastEncountered) return;

            using (var session = _store.OpenSession())
            {
                await _projection.ApplyAsync(session, page.Streams, cancellation).ConfigureAwait(false);

                session.QueueOperation(new EventProgressWrite(_events, _projection.Produces.FullName, page.To));

                await session.SaveChangesAsync(cancellation).ConfigureAwait(false);

                _logger.PageExecuted(page, this);

                LastEncountered = page.To;

                evaluateWaiters();

                UpdateBlock?.Post(new StoreProgress(_projection.Produces, page));
            }
        }

        private void evaluateWaiters()
        {
            var expiredWaiters = _waiters.Where(x => x.Sequence <= LastEncountered).ToArray();
            foreach (var waiter in expiredWaiters)
            {
                waiter.Completion.SetResult(LastEncountered);
                _waiters.Remove(waiter);
            }
        }

        public async Task CachePage(EventPage page)
        {
            Accumulator.Store(page);

            
            if (Accumulator.CachedEventCount > _projection.AsyncOptions.MaximumStagedEventCount)
            {
                _logger.ProjectionBackedUp(this, Accumulator.CachedEventCount, page);
                await _fetcher.Pause().ConfigureAwait(false);
            }
            

            _executionTrack.Post(page);
        }

        private bool shouldRestartFetcher()
        {
            if (_fetcher.State == FetcherState.Active) return false;

            if (Lifecycle == DaemonLifecycle.StopAtEndOfEventData && _atEndOfEventLog) return false;

            if (Accumulator.CachedEventCount <= _projection.AsyncOptions.CooldownStagedEventCount &&
                _fetcher.State == FetcherState.Paused)
            {
                return true;
            }

            return false;

        }

        public Task StoreProgress(Type viewType, EventPage page)
        {
            Accumulator.Prune(page.To);

            if (shouldRestartFetcher())
            {
                _fetcher.Start(this, Lifecycle);
            }

            return Task.CompletedTask;
        }

        private async Task clearExistingState(CancellationToken token)
        {
            _logger.ClearingExistingState(this);

            var tableName = _store.Schema.MappingFor(_projection.Produces).Table;
            var sql =
                $"delete from {_store.Schema.Events.DatabaseSchemaName}.mt_event_progression where name = :name;truncate {tableName} cascade";

            using (var conn = _store.Advanced.OpenConnection())
            {
                await conn.ExecuteAsync(async (cmd, tkn) =>
                {
                    await cmd.Sql(sql)
                        .With("name", _projection.Produces.FullName)
                        .ExecuteNonQueryAsync(tkn)
                        .ConfigureAwait(false);
                }, token).ConfigureAwait(false);
            }

            LastEncountered = 0;
        }
    }

}