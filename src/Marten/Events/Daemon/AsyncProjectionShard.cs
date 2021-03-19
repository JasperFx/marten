using System.Linq;
using System.Threading;
using Marten.Events.Projections;
using Marten.Linq.SqlGeneration;
using Marten.Storage;

namespace Marten.Events.Daemon
{
    internal class AsyncProjectionShard: AsyncProjectionShardBase
    {
        private readonly IProjection _projection;

        public AsyncProjectionShard(ShardName identifier, IProjection projection, ISqlFragment[] eventFilters, DocumentStore store,
            AsyncOptions options): base(identifier, eventFilters, options)
        {
            _projection = projection;
        }

        public override EventRangeGroup GroupEvents(IDocumentStore documentStore, ITenancy storeTenancy,
            EventRange range,
            CancellationToken cancellationToken)
        {
            return new TenantedEventRange(documentStore, storeTenancy, _projection, range, cancellationToken);
        }
    }
}
