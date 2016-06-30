using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Util;

namespace Marten.Events.Projections.Async
{
    public class ProjectionTrack : IEventPageWorker, IProjectionTrack
    {
        private readonly IFetcher _fetcher;
        private readonly IDocumentSession _session;
        private readonly IProjection _projection;
        private bool _isDisposed;
        private readonly ActionBlock<EventPage> _executionTrack;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly IList<EventWaiter> _waiters = new List<EventWaiter>();
        private readonly TaskCompletionSource<long> _rebuildCompletion = new TaskCompletionSource<long>();
        private readonly EventGraph _events;
        private readonly IDocumentStore _store;

        public ProjectionTrack(IFetcher fetcher, IDocumentStore store, IProjection projection)
        {
            _fetcher = fetcher;
            _session = store.OpenSession();
            _projection = projection;
            _store = store;

            _events = store.Schema.Events;

            _executionTrack = new ActionBlock<EventPage>(page => ExecutePage(page, _cancellation.Token));

            UpdateBlock = new ActionBlock<IDaemonUpdate>(msg => msg.Invoke(this));

            ViewType = _projection.Produces;
        }

        public Type ViewType { get; }

        public ActionBlock<IDaemonUpdate> UpdateBlock { get; private set; }

        public Accumulator Accumulator { get; } = new Accumulator();

        public long LastEncountered { get; set; }

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            UpdateBlock.Complete();

            _waiters.Clear();
            _executionTrack.Complete();
        }

        public async Task ExecutePage(EventPage page, CancellationToken cancellation)
        {
            await _projection.ApplyAsync(_session, page.Streams, cancellation).ConfigureAwait(false);

            _session.QueueOperation(new EventProgressWrite(_events, _projection.Produces.FullName, page.To));

            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);

            Console.WriteLine($"Processed {page} for view {_projection.Produces.FullName}");

            LastEncountered = page.To;

            evaluateWaiters();

            UpdateBlock?.Post(new StoreProgress(_projection.Produces, page));
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

        void IEventPageWorker.QueuePage(EventPage page)
        {
            UpdateBlock.Post(new CachePageUpdate(page));
        }

        void IEventPageWorker.Finished(long lastEncountered)
        {
            WaitUntilEventIsProcessed(lastEncountered).ContinueWith(t =>
            {
                _fetcher.Stop();
;                _rebuildCompletion.SetResult(lastEncountered);
            });

            Dispose();
        }

        public void Start(DaemonLifecycle lifecycle)
        {
            _store.Schema.EnsureStorageExists(_projection.Produces);

            Lifecycle = lifecycle;
            _fetcher.Start(this, lifecycle);
        }

        public async Task Stop()
        {
            await _fetcher.Stop().ConfigureAwait(false);

        }

        public async Task CachePage(EventPage page)
        {
            Accumulator.Store(page);

            if (Accumulator.CachedEventCount > _projection.AsyncOptions.MaximumStagedEventCount)
            {
                await _fetcher.Pause().ConfigureAwait(false);
            }

            _executionTrack.Post(page);
        }

        public Task StoreProgress(Type viewType, EventPage page)
        {
            Accumulator.Prune(page.To);

            if (Accumulator.CachedEventCount <= _projection.AsyncOptions.MaximumStagedEventCount &&
                _fetcher.State == FetcherState.Paused)
            {
                _fetcher.Start(this, Lifecycle);
            }

            return Task.CompletedTask;
        }

        public DaemonLifecycle Lifecycle { get; private set; } = DaemonLifecycle.Continuous;

        public Task<long> WaitUntilEventIsProcessed(long sequence)
        {
            if (LastEncountered >= sequence) return Task.FromResult(sequence);

            var waiter = new EventWaiter(sequence);
            _waiters.Add(waiter);

            return waiter.Completion.Task;
        }

        public Task<long> RunUntilEndOfEvents(CancellationToken token = new CancellationToken())
        {
            _store.Schema.EnsureStorageExists(_projection.Produces);

            // TODO -- make the CancellationToken go into fetcher
            _fetcher.Start(this, DaemonLifecycle.StopAtEndOfEventData);
            return _rebuildCompletion.Task;
        }

        public async Task Rebuild(CancellationToken token = new CancellationToken())
        {
            _store.Schema.EnsureStorageExists(_projection.Produces);

            await _fetcher.Stop().ConfigureAwait(false);

            await clearExistingState(token).ConfigureAwait(false);

            await RunUntilEndOfEvents(token).ConfigureAwait(false);
        }

        private async Task clearExistingState(CancellationToken token)
        {
            var tableName = _store.Schema.MappingFor(_projection.Produces).Table;
            var sql = $"delete from {_store.Schema.Events.DatabaseSchemaName}.mt_event_progression where name = :name;truncate {tableName} cascade";

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

            Console.WriteLine("Cleared the Existing Projection State for " + _projection.Produces.FullName);
        }
    }
}