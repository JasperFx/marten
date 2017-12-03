using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Projections.Async.ErrorHandling;
using Marten.Storage;
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
        private readonly IDaemonErrorHandler _errorHandler;
        private readonly ITenant _tenant;
        private readonly IProjection _projection;
        private readonly TaskCompletionSource<long> _rebuildCompletion = new TaskCompletionSource<long>();
        private readonly DocumentStore _store;
        private readonly IList<EventWaiter> _waiters = new List<EventWaiter>();
        private bool _isDisposed;
        private bool _atEndOfEventLog;

        public ProjectionTrack(IFetcher fetcher, DocumentStore store, IProjection projection, IDaemonLogger logger, IDaemonErrorHandler errorHandler, ITenant tenant)
        {
            _fetcher = fetcher;
            _projection = projection;
            _logger = logger;
            _errorHandler = errorHandler;
            _tenant = tenant;
            _store = store;

            _events = store.Events;

            ViewType = _projection.ProjectedType();
        }

        private void startConsumers()
        {
            _executionTrack = new ActionBlock<EventPage>(page => ExecutePage(page, _cancellation.Token),
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = 1,
#if !NET46
                    EnsureOrdered = true
#endif
                });

            UpdateBlock = new ActionBlock<IDaemonUpdate>(msg => msg.Invoke(this), new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1,
#if !NET46
                EnsureOrdered = true
#endif
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
            UpdateBlock?.Complete();
            _executionTrack?.Complete();
        }

        public void QueuePage(EventPage page)
        {
            UpdateBlock.Post(new CachePageUpdate(page));
        }

        public void Finished(long lastEncountered)
        {
            _logger.FetchingFinished(this, lastEncountered);
            _atEndOfEventLog = true;

            if (_executionTrack.InputCount == 0 && LastEncountered >= lastEncountered)
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

        public void EnsureStorageExists(ITenant tenant)
        {
            _projection.EnsureStorageExists(tenant);
        }

        public void Start(DaemonLifecycle lifecycle)
        {
            _logger.StartingProjection(this, lifecycle);

            EnsureStorageExists(_tenant);

            startConsumers();

            Lifecycle = lifecycle;
            _fetcher.Start(this, lifecycle, _cancellation.Token);

            IsRunning = true;
        }

        public bool IsRunning { get; private set; }

        public async Task Stop()
        {
            _logger.Stopping(this);
            stopConsumers();
            await _fetcher.Stop().ConfigureAwait(false);
            _logger.Stopped(this);

            IsRunning = false;

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
            ensureStorageExists();
            

            Start(DaemonLifecycle.StopAtEndOfEventData);

            return _rebuildCompletion.Task;
        }

        private void ensureStorageExists()
        {
            _projection.EnsureStorageExists(_tenant);            
        }

        public async Task Rebuild(CancellationToken token = new CancellationToken())
        {
            Lifecycle = DaemonLifecycle.StopAtEndOfEventData;

            ensureStorageExists();

            await _fetcher.Stop().ConfigureAwait(false);

            

            await _errorHandler.TryAction(async () =>
            {
                await clearExistingState(token).ConfigureAwait(false);
                _fetcher.Reset();
            }, this).ConfigureAwait(false);

            
            await RunUntilEndOfEvents(token).ConfigureAwait(false);
        }

        public async Task ExecutePage(EventPage page, CancellationToken cancellation)
        {
            // Duplicated, ignore. Shouldn't happen, but Fetcher is screwed up, so...
            if (page.To <= LastEncountered) return;

            await _errorHandler.TryAction(async () =>
            {
                await executePage(page, cancellation).ConfigureAwait(false);
            }, this).ConfigureAwait(false);


        }

        private async Task executePage(EventPage page, CancellationToken cancellation)
        {
            // TODO -- have to pass in the tenant here
            using (var session = _store.OpenSession())
            {
                await _projection.ApplyAsync(session, page, cancellation).ConfigureAwait(false);

                session.QueueOperation(new EventProgressWrite(_events, _projection.ProjectedType().FullName, page.To));

                await session.SaveChangesAsync(cancellation).ConfigureAwait(false);

                _logger.PageExecuted(page, this);

                // This is a change to accomodate the big gap problem
                LastEncountered = page.LastEncountered();

                evaluateWaiters();

                UpdateBlock?.Post(new StoreProgress(_projection.ProjectedType(), page));
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


            _executionTrack?.Post(page);
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
            var types = _projection.ProjectedTypes();
            
            using (var conn = _tenant.OpenConnection())
            {
                foreach (var type in types)
                {
                    var tableName = _tenant.MappingFor(type).Table;
                    var sql =
                        $"delete from {_store.Events.DatabaseSchemaName}.mt_event_progression where name = :name;truncate {tableName} cascade";

                    await conn.ExecuteAsync(async (cmd, tkn) =>
                    {
                        await cmd.Sql(sql)
                            .With("name", type.FullName)
                            .ExecuteNonQueryAsync(tkn)
                            .ConfigureAwait(false);
                    }, token);
                }                
            }
            LastEncountered = 0;
        }
    }

}