using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events.Projections.Async
{
    public class Fetcher : IDisposable, IFetcher
    {
        private readonly AsyncOptions _options;
        private readonly NpgsqlConnection _conn;
        private readonly NulloIdentityMap _map;
        private readonly EventSelector _selector;
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private FetcherState _state;
        private Task _fetchingTask;
        private long _lastEncountered = 0;
        private readonly string[] _eventTypeNames;

        public Fetcher(IDocumentStore store, AsyncOptions options, IEnumerable<Type> eventTypes)
        {
            _options = options;
            _state = FetcherState.Waiting;

            _conn = store.Schema.StoreOptions.ConnectionFactory().Create();

            _conn.Open();

            _selector = new EventSelector(store.Schema.Events, store.Advanced.Serializer);
            _map = new NulloIdentityMap(store.Advanced.Serializer);

            _eventTypeNames = eventTypes.Select(x => store.Schema.Events.EventMappingFor(x).Alias).ToArray();
        }

        public Fetcher(IDocumentStore store, IProjection projection) : this(store, projection.AsyncOptions, projection.Consumes)
        {
            
        }

        public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();


        public string[] EventTypeNames { get; set; } = new string[0];

        public void Start(IEventPageWorker worker, DaemonLifecycle lifecycle)
        {
            _lock.Write(() =>
            {
                if (_state == FetcherState.Active) return;

                _state = FetcherState.Active;

                

                _fetchingTask = Task.Factory.StartNew(async () =>
                {
                    while (!Cancellation.IsCancellationRequested && _state == FetcherState.Active)
                    {
                        var page = await FetchNextPage(_lastEncountered).ConfigureAwait(false);

                        if (page.Count == 0)
                        {
                            if (lifecycle == DaemonLifecycle.Continuous)
                            {
                                _state = FetcherState.Waiting;
                                
                                // TODO -- make the cooldown time be configurable
                                await Task.Delay(1.Seconds(), Cancellation.Token).ConfigureAwait(false);
                                Start(worker, lifecycle);
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
                }, Cancellation.Token);
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
            if (_state != FetcherState.Active)
            {
                return;
            }

            _lock.EnterWriteLock();
            try
            {
                _state = FetcherState.Waiting;

                Cancellation.Cancel();

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
            Cancellation.Cancel();
            _conn.Close();
            _conn.Dispose();
        }

        public async Task<EventPage> FetchNextPage(long lastEncountered)
        {
            var lastPossible = lastEncountered + _options.PageSize;
            var sql =
                $@"
select max(seq_id) from mt_events where seq_id > :last and seq_id <= :limit;
{_selector
                    .ToSelectClause(null)} where seq_id > :last and seq_id <= :limit and type = ANY(:types) order by seq_id;       
";

            var cmd = _conn.CreateCommand(sql)
                .With("last", lastEncountered)
                .With("limit", lastPossible)
                .With("types", _eventTypeNames, NpgsqlDbType.Array | NpgsqlDbType.Varchar);


            long furthestExtant;
            IList<IEvent> events = null;

            var token = Cancellation.Token;
            using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                await reader.ReadAsync(token).ConfigureAwait(false);

                furthestExtant = await reader.IsDBNullAsync(0, token).ConfigureAwait(false)
                    ? 0
                    : await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);

                await reader.NextResultAsync(token).ConfigureAwait(false);

                events = await _selector.ReadAsync(reader, _map, token).ConfigureAwait(false);
            }


            var streams =
                events.GroupBy(x => x.StreamId)
                    .Select(
                        group => { return new EventStream(group.Key, group.OrderBy(x => x.Version).ToArray(), false); })
                    .ToArray();

            return new EventPage(lastEncountered, furthestExtant, streams) { Count = events.Count };
        }
    }
}