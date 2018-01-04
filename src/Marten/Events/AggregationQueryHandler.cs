using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Events
{
    internal class AggregationQueryHandler<T> : IQueryHandler<T> where T : class, new()
    {
        private readonly IAggregator<T> _aggregator;
        private readonly IEventQueryHandler _inner;
        private readonly IDocumentSession _session;
        private readonly T _state;

        public AggregationQueryHandler(IAggregator<T> aggregator, IEventQueryHandler inner, IDocumentSession session = null, T state = null)
        {
            _aggregator = aggregator;
            _inner = inner;
            _session = session;
            _state = state;
        }

        public void ConfigureCommand(CommandBuilder builder)
        {
            _inner.ConfigureCommand(builder);
        }

        public Type SourceType => typeof (IEvent);

        public T Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            var @events = _inner.Handle(reader, map, stats);

            return _state == null ? _aggregator.Build(@events, _session) : _aggregator.Build(@events, _session, _state);
        }

        public async Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var @events = await _inner.HandleAsync(reader, map, stats, token).ConfigureAwait(false);

            return _state == null ? _aggregator.Build(@events, _session) : _aggregator.Build(@events, _session, _state);
        }
    }
}