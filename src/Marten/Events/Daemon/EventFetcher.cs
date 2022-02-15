using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Fetches ranges of event objects. Used within the asynchronous projections
    /// </summary>
    internal class EventFetcher : IDisposable
    {
        private readonly IDocumentStore _store;
        private readonly ISqlFragment[] _filters;
        private IEventStorage _storage;
        private EventStatement _statement;
        private IQueryHandler<IReadOnlyList<IEvent>> _handler;

        public EventFetcher(IDocumentStore store, ISqlFragment[] filters)
        {
            _store = store;
            _filters = filters;

            using var session = (QuerySession)_store.QuerySession(new SessionOptions{AllowAnyTenant = true});
            _storage = session.EventStorage();
            _statement = new EventStatement(_storage) {Filters = _filters};

            _handler = new ListQueryHandler<IEvent>(_statement, _storage);
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
            using var session = (QuerySession)_store.QuerySession(new SessionOptions{AllowAnyTenant = true});

            // There's an assumption here that this method is only called sequentially
            // and never at the same time on the same instance
            _statement.Range = range;

            try
            {
                range.Events = new List<IEvent>(await session.ExecuteHandlerAsync(_handler, token).ConfigureAwait(false));
            }
            catch (Exception e)
            {
                throw new EventFetcherException(projectionShardName, e);
            }
        }
    }
}
