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

        protected override Task configureUpdateBatch(IProjectionAgent projectionAgent, ProjectionUpdateBatch batch,
            TenantedEventRange @group,
            CancellationToken token)
        {
            var tasks = group.Groups.Select(tenantGroup =>
            {
                return projectionAgent.TryAction(async () =>
                {
                    await tenantGroup.ApplyEvents(batch, _projection, Store, token);
                }, token);
            }).ToArray();

            return Task.WhenAll(tasks);
        }

        protected override TenantedEventRange applyGrouping(EventRange range)
        {
            return new(Store.Events, Store.Tenancy, range);
        }
    }

    internal class TenantedEventRange: IEventRangeGroup
    {
        public TenantedEventRange(EventGraph graph, ITenancy tenancy, EventRange range)
        {
            Range = range;

            var byTenant = range.Events.GroupBy(x => x.TenantId);
            foreach (var group in byTenant)
            {
                var tenant = tenancy[group.Key];

                var actions = graph.StreamIdentity switch
                {
                    StreamIdentity.AsGuid => group.GroupBy(x => x.StreamId)
                        .Select(events => StreamAction.For(events.Key, events.ToList())),

                    StreamIdentity.AsString => group.GroupBy(x => x.StreamKey)
                        .Select(events => StreamAction.For(events.Key, events.ToList())),

                    _ => null
                };

                Groups.Add(new TenantActionGroup(tenant, actions));
            }
        }

        public IList<TenantActionGroup> Groups { get; } = new List<TenantActionGroup>();
        public EventRange Range { get; }
        public void Reset()
        {

        }

        public void Dispose()
        {
        }

        public override string ToString()
        {
            return $"Tenant Group Range for: {Range}";
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
