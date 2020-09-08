using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Marten.Util;

namespace Marten.Events
{
    internal class AggregationQueryHandler<T>: IQueryHandler<T> where T : class
    {
        private readonly IAggregator<T> _aggregator;
        private readonly IEventQueryHandler _inner;
        private readonly QuerySession _session;
        private readonly T _state;

        public AggregationQueryHandler(IAggregator<T> aggregator, IEventQueryHandler inner, QuerySession session = null, T state = null)
        {
            _aggregator = aggregator;
            _inner = inner;
            _session = session;
            _state = state;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            _inner.ConfigureCommand(builder, session);
        }


        public T Handle(DbDataReader reader, IMartenSession session)
        {
            var @events = _inner.Handle(reader, session);

            return _state == null ? _aggregator.Build(@events, (IDocumentSession) _session) : _aggregator.Build(@events, (IDocumentSession) _session, _state);
        }

        public async Task<T> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var @events = await _inner.HandleAsync(reader, session, token).ConfigureAwait(false);

            return _state == null ? _aggregator.Build(@events, (IDocumentSession) _session) : _aggregator.Build(@events, (IDocumentSession) _session, _state);
        }
    }
}
