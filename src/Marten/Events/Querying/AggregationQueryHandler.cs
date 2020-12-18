using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Events.V4Concept;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Util;

namespace Marten.Events.Querying
{
    internal class AggregationQueryHandler<T>: IQueryHandler<T> where T : class
    {
        private readonly ILiveAggregator<T> _aggregator;
        private readonly IEventQueryHandler _inner;
        private readonly QuerySession _session;
        private readonly T _snapshot;

        public AggregationQueryHandler(ILiveAggregator<T> aggregator, IEventQueryHandler inner, QuerySession session = null, T snapshot = null)
        {
            _aggregator = aggregator;
            _inner = inner;
            _session = session;
            _snapshot = snapshot;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            _inner.ConfigureCommand(builder, session);
        }


        public T Handle(DbDataReader reader, IMartenSession session)
        {
            var @events = _inner.Handle(reader, session);
            return @events.Any()
                ? _aggregator.Build(@events, _session, _snapshot)
                : null;
        }

        public async Task<T> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var @events = await _inner.HandleAsync(reader, session, token).ConfigureAwait(false);

            return @events.Any()
                ? await _aggregator.BuildAsync(@events, _session, _snapshot, token)
                : null;
        }
    }
}
