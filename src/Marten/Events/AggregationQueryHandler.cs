using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Npgsql;

namespace Marten.Events
{
    internal class AggregationQueryHandler<T> : IQueryHandler<T> where T : class, new()
    {
        private readonly IAggregator<T> _aggregator;
        private readonly EventQueryHandler _inner;

        public AggregationQueryHandler(IAggregator<T> aggregator, EventQueryHandler inner)
        {
            _aggregator = aggregator;
            _inner = inner;
        }

        public void ConfigureCommand(NpgsqlCommand command)
        {
            _inner.ConfigureCommand(command);
        }

        public Type SourceType => typeof (IEvent);

        public T Handle(DbDataReader reader, IIdentityMap map)
        {
            var @events = _inner.Handle(reader, map);

            return _aggregator.Build(@events);
        }

        public async Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var @events = await _inner.HandleAsync(reader, map, token).ConfigureAwait(false);

            return _aggregator.Build(@events);
        }
    }
}