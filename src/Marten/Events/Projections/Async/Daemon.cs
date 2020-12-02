using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async.ErrorHandling;
using Marten.Storage;
using Marten.Util;
using Npgsql;

namespace Marten.Events.Projections.Async
{
    public class Daemon: IDaemon
    {
        private readonly DocumentStore _store;
        private readonly ITenant _tenant;
        private readonly IDictionary<Type, IProjectionTrack> _tracks = new Dictionary<Type, IProjectionTrack>();
        private readonly DaemonErrorHandler _errorHandler;

        public Daemon(DocumentStore store, ITenant tenant, DaemonSettings settings, IDaemonLogger logger, IEnumerable<IProjection> projections)
        {
            _store = store;
            _tenant = tenant;
            Logger = logger;

            _errorHandler = new DaemonErrorHandler(this, logger, settings.ExceptionHandling);

            foreach (var projection in projections)
            {
                if (projection == null)
                {
                    throw new ArgumentOutOfRangeException(nameof(projection), $"No projection is configured");
                }

                var fetcher = new Fetcher(store, settings, projection, logger, _errorHandler);
                var track = new ProjectionTrack(fetcher, store, projection, logger, _errorHandler, tenant);

                _tracks.Add(projection.ProjectedType(), track);
            }
        }

        public IDaemonLogger Logger { get; }

        public async Task StopAll()
        {
            Logger.BeginStopAll();
            foreach (var track in _tracks.Values)
            {
                await track.Stop().ConfigureAwait(false);
            }

            Logger.AllStopped();
        }

        public Task Stop<T>()
        {
            return Stop(typeof(T));
        }

        public Task Stop(Type viewType)
        {
            if (!_tracks.TryGetValue(viewType, out var track))
            {
                return Task.CompletedTask;
            }

            return track.Stop();
        }

        public void Start<T>(DaemonLifecycle lifecycle)
        {
            Start(typeof(T), lifecycle);
        }

        public void Start(Type viewType, DaemonLifecycle lifecycle)
        {
            if (!_tracks.TryGetValue(viewType, out var track))
            {
                throw new ArgumentOutOfRangeException(nameof(viewType));
            }

            findCurrentEventLogPosition(viewType);

            track.Start(lifecycle);
        }

        public void Dispose()
        {
            foreach (var track in _tracks.Values)
            {
                track.Dispose();
            }

            _tracks.Clear();
        }

        public void StartAll()
        {
            Logger.BeginStartAll(_tracks.Values);

            foreach (var track in _tracks.Values)
            {
                track.EnsureStorageExists(_tenant);
            }

            _tenant.EnsureStorageExists(typeof(EventStream));

            findCurrentEventLogPositions();

            foreach (var track in _tracks.Values)
            {
                track.Start(DaemonLifecycle.Continuous);
            }

            Logger.FinishedStartingAll();
        }

        private void findCurrentEventLogPositions()
        {
            using (var conn = _tenant.OpenConnection())
            {
                var cmd = new NpgsqlCommand($"select name, last_seq_id from {_store.Events.ProgressionTable}");
                using (var reader = conn.ExecuteReader(cmd))
                {
                    while (reader.Read())
                    {
                        var name = reader.GetFieldValue<string>(0);
                        var lastEncountered = reader.GetFieldValue<long>(1);

                        var track = _tracks.Values.FirstOrDefault(x => x.ProgressionName == name);

                        if (track != null)
                        {
                            track.LastEncountered = lastEncountered;
                        }
                    }
                }

            }

            foreach (var track in _tracks.Values)
            {
                Logger.DeterminedStartingPosition(track);
            }
        }

        private void findCurrentEventLogPosition(Type viewType)
        {
            using (var conn = _tenant.OpenConnection())
            {
                var projectionTrack = _tracks[viewType];
                var cmd = new NpgsqlCommand($"select last_seq_id from {_store.Events.ProgressionTable} where name = :name").With("name", projectionTrack.ProgressionName);

                using (var reader = conn.ExecuteReader(cmd))
                {
                    if (reader.Read())
                    {
                        var lastEncountered = reader.GetFieldValue<long>(0);
                        projectionTrack.LastEncountered = lastEncountered;
                    }
                }
            }

            foreach (var track in _tracks.Values)
            {
                Logger.DeterminedStartingPosition(track);
            }
        }

        public Task WaitUntilEventIsProcessed(long sequence, CancellationToken token = new CancellationToken())
        {
            if (_tracks.Values.Any(x => !x.IsRunning))
            {
                throw new InvalidOperationException("This daemon has not been started");
            }

            var tasks = _tracks.Values.Select(x => x.WaitUntilEventIsProcessed(sequence));
            return Task.WhenAll(tasks);
        }

        public async Task WaitForNonStaleResults(CancellationToken token = new CancellationToken())
        {
            if (_tracks.Values.Any(x => !x.IsRunning))
            {
                throw new InvalidOperationException("This daemon has not been started");
            }

            var last = await currentEventNumber(token).ConfigureAwait(false);

            await WaitUntilEventIsProcessed(last, token).ConfigureAwait(false);
        }

        private void assertStarted(Type viewType)
        {
            if (_tracks.TryGetValue(viewType, out var track))
            {
                if (!track.IsRunning)
                {
                    throw new InvalidOperationException($"The projection track for view {viewType.FullName} has not been started");
                }
            }
        }

        public Task WaitForNonStaleResultsOf<T>(CancellationToken token = new CancellationToken())
        {
            return WaitForNonStaleResultsOf(typeof(T), token);
        }

        public async Task WaitForNonStaleResultsOf(Type viewType, CancellationToken token)
        {
            assertStarted(viewType);

            if (!_tracks.TryGetValue(viewType, out var track))
            {
                throw new ArgumentOutOfRangeException(nameof(viewType));
            }

            var current = await currentEventNumber(token).ConfigureAwait(false);

            await track.WaitUntilEventIsProcessed(current).ConfigureAwait(false);
        }

        public IEnumerable<IProjectionTrack> AllActivity => _tracks.Values;

        public IProjectionTrack TrackFor<T>()
        {
            return TrackFor(typeof(T));
        }

        public IProjectionTrack TrackFor(Type viewType)
        {
            if (_tracks.TryGetValue(viewType, out var track))
            {
                return track;
            }
            return null;
        }

        public Task RebuildAll(CancellationToken token = new CancellationToken())
        {
            Logger.BeginRebuildAll(_tracks.Values);
            var all = _tracks.Values.Select(x => x.Rebuild(token));

            return Task.WhenAll(all).ContinueWith(t =>
            {
                Logger.FinishRebuildAll(t.Status, t.Exception);
            }, token);
        }

        public Task Rebuild<T>(CancellationToken token = new CancellationToken())
        {
            return Rebuild(typeof(T), token);
        }

        public Task Rebuild(Type viewType, CancellationToken token = new CancellationToken())
        {
            if (!_tracks.TryGetValue(viewType, out var track))
            {
                throw new ArgumentOutOfRangeException(nameof(viewType));
            }

            return track.Rebuild(token);
        }

        private async Task<long> currentEventNumber(CancellationToken token)
        {
            await using (var conn = _tenant.OpenConnection())
            {
                var cmd = new NpgsqlCommand($"select max(seq_id) from {_store.Events.Table}");

                using (var reader = await conn.ExecuteReaderAsync(cmd, token).ConfigureAwait(false))
                {
                    var any = await reader.ReadAsync(token).ConfigureAwait(false);
                    if (!any)
                        return 0;

                    if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
                    {
                        return 0;
                    }

                    return await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
                }
            }
        }
    }
}
