using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Storage;

namespace Marten.Events.Daemon
{
    internal class TenantedEventRange: EventRangeGroup
    {
        private DocumentStore _store;
        private readonly IProjection _projection;

        public TenantedEventRange(DocumentStore store, IProjection projection, EventRange range,
            CancellationToken shardCancellation) : base(range, shardCancellation)
        {
            _store = store;
            _projection = projection;

            var byTenant = range.Events.GroupBy(x => x.TenantId);
            foreach (var group in byTenant)
            {
                var tenant = _store.Tenancy[group.Key];

                var actions = _store.Events.StreamIdentity switch
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

        protected override void reset()
        {
            // Nothing
        }

        public override void Dispose()
        {
            // Nothing
        }

        public override string ToString()
        {
            return $"Tenant Group Range for: {Range}";
        }

        public override Task ConfigureUpdateBatch(IProjectionAgent projectionAgent, ProjectionUpdateBatch batch)
        {
            var tasks = Groups.Select(tenantGroup =>
            {
                return projectionAgent.TryAction(async () =>
                {
                    await tenantGroup.ApplyEvents(batch, _projection, _store, Cancellation);
                }, Cancellation);
            }).ToArray();

            return Task.WhenAll(tasks);
        }
    }
}
