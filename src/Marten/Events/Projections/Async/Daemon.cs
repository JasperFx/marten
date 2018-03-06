using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async.ErrorHandling;
using Marten.Storage;
using Marten.Util;

namespace Marten.Events.Projections.Async
{
    public class Daemon : IDaemon
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
                    throw new ArgumentOutOfRangeException(nameof(projection),
                        $"No projection is configured for view type {projection.ProjectedType().FullName}");

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
            if (!_tracks.ContainsKey(viewType))
            {
                return Task.CompletedTask;
            }

            return _tracks[viewType].Stop();
        }

        public void Start<T>(DaemonLifecycle lifecycle)
        {
            Start(typeof(T), lifecycle);
        }

        public void Start(Type viewType, DaemonLifecycle lifecycle)
        {
            if (!_tracks.ContainsKey(viewType))
            {
                throw new ArgumentOutOfRangeException(nameof(viewType));
            }

            findCurrentEventLogPosition(viewType);

            _tracks[viewType].Start(lifecycle);
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
                conn.Execute(cmd =>
                {
                    cmd.Sql($"select name, last_seq_id from {_store.Events.ProgressionTable}");

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var name = reader.GetFieldValue<string>(0);
                            var lastEncountered = reader.GetFieldValue<long>(1);

                            var track = _tracks.Values.FirstOrDefault(x => x.ViewType.FullName == name);

                            if (track != null)
                            {
                                track.LastEncountered = lastEncountered;
                            }
                        }
                    }
                });
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
                conn.Execute(cmd =>
                {
                    cmd.Sql($"select last_seq_id from {_store.Events.ProgressionTable} where name = :name").With("name", viewType.FullName);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var lastEncountered = reader.GetFieldValue<long>(0);
                            _tracks[viewType].LastEncountered = lastEncountered;
                        }
                    }
                });
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
            if (_tracks.ContainsKey(viewType))
            {
                if (!_tracks[viewType].IsRunning)
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

            if (!_tracks.ContainsKey(viewType))
            {
                throw new ArgumentOutOfRangeException(nameof(viewType));
            }

            var current = await currentEventNumber(token).ConfigureAwait(false);

            await _tracks[viewType].WaitUntilEventIsProcessed(current).ConfigureAwait(false);
        }

        public IEnumerable<IProjectionTrack> AllActivity => _tracks.Values;

        public IProjectionTrack TrackFor<T>()
        {
            return TrackFor(typeof(T));
        }

        public IProjectionTrack TrackFor(Type viewType)
        {
            return _tracks.ContainsKey(viewType) ? _tracks[viewType] : null;
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
            if (!_tracks.ContainsKey(viewType))
            {
                throw new ArgumentOutOfRangeException(nameof(viewType));
            }

            return _tracks[viewType].Rebuild(token);
        }

        private async Task<long> currentEventNumber(CancellationToken token)
        {
            using (var conn = _tenant.OpenConnection())
            {
                return await conn.ExecuteAsync(async (cmd, tkn) =>
                {
                    cmd.Sql($"select max(seq_id) from {_store.Events.Table}");
                    using (var reader = await cmd.ExecuteReaderAsync(tkn).ConfigureAwait(false))
                    {
                        var any = await reader.ReadAsync(tkn).ConfigureAwait(false);
                        if (!any) return 0;

                        if (await reader.IsDBNullAsync(0, tkn).ConfigureAwait(false))
                        {
                            return 0;
                        }

                        return await reader.GetFieldValueAsync<long>(0, tkn).ConfigureAwait(false);
                    }
                }, token).ConfigureAwait(false);
            }
        }
    }
}