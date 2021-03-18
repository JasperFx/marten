using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Marten.Storage;

namespace Marten.Events.Daemon
{
    internal class TenantedEventRange: EventRangeGroup
    {
        public TenantedEventRange(EventGraph graph, ITenancy tenancy, EventRange range, CancellationToken shardCancellation) : base(range, shardCancellation)
        {
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
    }
}
