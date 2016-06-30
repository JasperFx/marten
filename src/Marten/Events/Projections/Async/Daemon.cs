using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Util;

namespace Marten.Events.Projections.Async
{
    public class Daemon : IDaemon
    {
        private readonly IDocumentStore _store;
        private readonly IDictionary<Type, IProjectionTrack> _tracks = new Dictionary<Type, IProjectionTrack>();

        public Daemon(IDocumentStore store, Type[] viewTypes)
        {
            _store = store;
            foreach (var viewType in viewTypes)
            {
                var projection = store.Schema.Events.AsyncProjections.ForView(viewType);
                if (projection == null)
                    throw new ArgumentOutOfRangeException(nameof(viewType),
                        $"No projection is configured for view type {viewType.FullName}");

                var fetcher = new Fetcher(store, projection);
                var track = new ProjectionTrack(fetcher, store, projection);

                _tracks.Add(viewType, track);
            }
        }

        public async Task StopAll()
        {
            foreach (var track in _tracks.Values)
            {
                await track.Stop().ConfigureAwait(false);
            }
        }

        public Task Stop<T>()
        {
            throw new NotImplementedException();
        }

        public Task Stop(Type viewType)
        {
            throw new NotImplementedException();
        }

        public void Start<T>()
        {
            using (var conn = _store.Advanced.OpenConnection())
            {
                conn.Execute(cmd =>
                {
                    cmd.Sql($"select name, last_seq_id from {_store.Schema.Events.ProgressionTable}");

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
                track.Start(DaemonLifecycle.Continuous);
            }
        }

        public void Start(Type viewType)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            foreach (var track in _tracks.Values)
            {
                track.Dispose();
            }

            _tracks.Clear();
        }

        public Task RebuildProjection<T>(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public void StartAll()
        {
            foreach (var track in _tracks.Values)
            {
                track.Start(DaemonLifecycle.Continuous);
            }
        }

        public Task<long> WaitUntilEventIsProcessed(long sequence)
        {
            throw new NotImplementedException();
        }

        public Task WaitForNonStaleResults()
        {
            throw new NotImplementedException();
        }

        public Task WaitForNonStaleResultsOf<T>()
        {
            throw new NotImplementedException();
        }

        public Task WaitForNonStaleResultsOf(Type viewType)
        {
            throw new NotImplementedException();
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

        public Task RebuildAll()
        {
            throw new NotImplementedException();
        }
    }
}