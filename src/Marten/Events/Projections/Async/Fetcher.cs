using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Services;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Events.Projections.Async
{
    public class Fetcher : IDisposable, IFetcher
    {
        private readonly IConnectionFactory _connectionFactory;
        private readonly string[] _eventTypeNames;
        private readonly IDaemonLogger _logger;
        private readonly NulloIdentityMap _map;
        private readonly AsyncOptions _options;
        private readonly EventSelector _selector;
        private Task _fetchingTask;
        private long _lastEncountered;

        public Fetcher(IDocumentStore store, AsyncOptions options, IDaemonLogger logger, IEnumerable<Type> eventTypes)
        {
            _options = options;
            _logger = logger;
            State = FetcherState.Waiting;

            _connectionFactory = store.Advanced.Options.ConnectionFactory();

            _selector = new EventSelector(store.Schema.Events, store.Advanced.Serializer);
            _map = new NulloIdentityMap(store.Advanced.Serializer);

            _eventTypeNames = eventTypes.Select(x => store.Schema.Events.EventMappingFor(x).Alias).ToArray();
        }

        public Fetcher(IDocumentStore store, IProjection projection, IDaemonLogger logger)
            : this(store, projection.AsyncOptions, logger, projection.Consumes)
        {
        }

        public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();


        public string[] EventTypeNames { get; set; } = new string[0];

        public void Dispose()
        {
            Cancellation.Cancel();
        }

        public void Start(IProjectionTrack track, DaemonLifecycle lifecycle)
        {
            if (_fetchingTask != null && !_fetchingTask.IsCompleted)
            {
                throw new InvalidOperationException("The Fetcher is already started!");
            }

            if (State == FetcherState.Active) return;

            State = FetcherState.Active;

            if (track.LastEncountered > _lastEncountered)
            {
                _lastEncountered = track.LastEncountered;
            }

            _logger.FetchStarted(track);

            _fetchingTask =
                Task.Factory.StartNew(async () =>
                {
                    await fetchEvents(track, lifecycle).ConfigureAwait(false);
                },
                    Cancellation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default)
                    .ContinueWith(t =>
                    {
                        _logger.FetchingStopped(track);
                    });
        }


        public async Task Pause()
        {
            State = FetcherState.Paused;

            await _fetchingTask.ConfigureAwait(false);
        }

        public async Task Stop()
        {
            if (State != FetcherState.Active)
            {
                return;
            }

            State = FetcherState.Waiting;

            Cancellation.Cancel();

            await _fetchingTask.ConfigureAwait(false);
        }

        public FetcherState State { get; private set; }

        public async Task<EventPage> FetchNextPage(long lastEncountered)
        {
            using (var conn = _connectionFactory.Create())
            {
                await conn.OpenAsync().ConfigureAwait(false);

                var lastPossible = lastEncountered + _options.PageSize;
                var sql =
                    $@"
select max(seq_id) from mt_events where seq_id > :last and seq_id <= :limit;
{_selector.ToSelectClause(null)} where seq_id > :last and seq_id <= :limit and type = ANY(:types) order by seq_id;       
";

                var cmd = conn.CreateCommand(sql)
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

                    reader.Close();
                }

                conn.Close();

                var streams = EventPage.ToStreams(events);

                return new EventPage(lastEncountered, furthestExtant, streams) {Count = events.Count};
            }
        }

        private async Task fetchEvents(IProjectionTrack track, DaemonLifecycle lifecycle)
        {
            while (!Cancellation.IsCancellationRequested && State == FetcherState.Active)
            {
                var page = await FetchNextPage(_lastEncountered).ConfigureAwait(false);

                _logger.PageFetched(track, page);

                if (page.Count == 0)
                {
                    if (lifecycle == DaemonLifecycle.Continuous)
                    {
                        State = FetcherState.Waiting;

                        _logger.PausingFetching(track, _lastEncountered);

                        // TODO -- make the cooldown time be configurable
#pragma warning disable 4014
                        Task.Delay(1.Seconds(), Cancellation.Token).ContinueWith(t =>
#pragma warning restore 4014
                        {
                            Start(track, lifecycle);
                        });
                    }
                    else
                    {
                        State = FetcherState.Paused;

                        _logger.FetchingIsAtEndOfEvents(track);
                        track.Finished(_lastEncountered);

                        break;
                    }
                }
                else
                {
                    _lastEncountered = page.To;
                    track.QueuePage(page);
                }
            }
        }
    }
}