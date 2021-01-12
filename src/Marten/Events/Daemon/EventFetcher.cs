using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Linq.SqlGeneration;

namespace Marten.Events.Daemon
{
    internal class EventFetcher : IDisposable
    {
        private readonly IDocumentStore _store;
        private readonly ISqlFragment[] _filters;
        private QuerySession _session;
        private IEventStorage _storage;
        private EventStatement _statement;
        private IQueryHandler<IReadOnlyList<IEvent>> _handler;
        private CancellationTokenSource _cancellation;

        public EventFetcher(IDocumentStore store, ISqlFragment[] filters)
        {
            _store = store;
            _filters = filters;

        }

        private void reset()
        {
            _session = (QuerySession)_store.QuerySession();
            _storage = _session.EventStorage();
            _statement = new EventStatement(_storage)
            {
                Filters = _filters
            };

            _handler = (IQueryHandler<IReadOnlyList<IEvent>>)new ListQueryHandler<IEvent>(_statement, _storage);

            _cancellation = new CancellationTokenSource(5.Seconds());
            _cancellation.Token.Register(teardown);
        }

        private void teardown()
        {
            _session?.Dispose();
            _session = null;
        }

        public void Dispose()
        {
            teardown();
        }

        public async Task Load(EventRange range, CancellationToken token)
        {
            if (_session == null)
            {
                reset();
            }

            _cancellation.CancelAfter(5.Seconds());

            // There's an assumption here that this method is only called sequentially
            // and never at the same time on the same instance
            _statement.Range = range;

            range.Events = await _session.ExecuteHandlerAsync(_handler, token);
        }
    }
}
