using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Linq.SqlGeneration;
using Marten.Storage;

namespace Marten.Events.Daemon
{
    internal class AsyncProjectionShard: AsyncProjectionShardBase<TenantedEventRange>
    {
        private readonly IProjection _projection;

        public AsyncProjectionShard(ShardName identifier, IProjection projection, ISqlFragment[] eventFilters, DocumentStore store,
            AsyncOptions options): base(identifier, eventFilters, store, options)
        {
            _projection = projection;
        }

        protected override TenantedEventRange applyGrouping(EventRange range)
        {
            return new(Store, _projection, range, Cancellation);
        }
    }

    internal class TenantActionGroup
    {
        private readonly List<StreamAction> _actions;
        private readonly ITenant _tenant;

        public TenantActionGroup(ITenant tenant, IEnumerable<StreamAction> actions)
        {
            _tenant = tenant;
            _actions = new List<StreamAction>(actions);
            foreach (var action in _actions) action.TenantId = _tenant.TenantId;
        }

        public Task ApplyEvents(ProjectionUpdateBatch batch, IProjection projection, DocumentStore store,
            CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                await using var operations = new ProjectionDocumentSession(store, _tenant, batch);

                await projection.ApplyAsync(operations, _actions, cancellationToken);
            }, cancellationToken);
        }
    }
}
