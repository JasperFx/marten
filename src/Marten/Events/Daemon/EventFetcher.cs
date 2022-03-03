using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Marten.Storage;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Fetches ranges of event objects. Used within the asynchronous projections
    /// </summary>
    internal class EventFetcher : IDisposable
    {
        private readonly IDocumentStore _store;
        private readonly IMartenDatabase _database;
        private readonly ISqlFragment[] _filters;
        private IEventStorage _storage;
        private readonly EventStatement _statement;
        private readonly IQueryHandler<IReadOnlyList<IEvent>> _handler;

        public EventFetcher(IDocumentStore store, IMartenDatabase database, ISqlFragment[] filters)
        {
            _store = store;
            _database = database;
            _filters = filters;

            using var session = querySession();
            _storage = session.EventStorage();
            _statement = new EventStatement(_storage) {Filters = _filters};

            _handler = new ListQueryHandler<IEvent>(_statement, _storage);
        }

        private QuerySession querySession()
        {
            return (QuerySession)_store.QuerySession(SessionOptions.ForDatabase(_database));
        }

        private void teardown()
        {

        }

        public void Dispose()
        {
            teardown();
        }

        public async Task Load(ShardName projectionShardName, EventRange range, CancellationToken token)
        {
            using var session = querySession();

            // There's an assumption here that this method is only called sequentially
            // and never at the same time on the same instance
            _statement.Range = range;

            try
            {
                range.Events = new List<IEvent>(await session.ExecuteHandlerAsync(_handler, token).ConfigureAwait(false));
            }
            catch (Exception e)
            {
                throw new EventFetcherException(projectionShardName, _database, e);
            }
        }
    }
}
