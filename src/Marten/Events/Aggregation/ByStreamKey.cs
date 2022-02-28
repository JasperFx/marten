using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Storage;
#nullable enable
namespace Marten.Events.Aggregation
{
    internal class ByStreamKey<TDoc>: IEventSlicer<TDoc, string>
    {
        public ValueTask<IReadOnlyList<EventSlice<TDoc, string>>> SliceInlineActions(IQuerySession querySession,
            IEnumerable<StreamAction> streams)
        {
            return new(streams.Select(s =>
            {
                var tenant = new Tenant(s.TenantId, querySession.Database);
                return new EventSlice<TDoc, string>(s.Key!, tenant, s.Events);
            }).ToList());
        }

        public async ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, string>>> SliceAsyncEvents(
            IQuerySession querySession,
            List<IEvent> events)
        {
            var list = new List<TenantSliceGroup<TDoc, string>>();
            var byTenant = events.GroupBy(x => x.TenantId);

            foreach (var tenantGroup in byTenant)
            {
                var tenant = new Tenant(tenantGroup.Key, querySession.Database);

                var slices = tenantGroup
                    .GroupBy(x => x.StreamKey)
                    .Select(x => new EventSlice<TDoc, string>(x.Key!, tenant, x));

                var group = new TenantSliceGroup<TDoc, string>(tenant, slices);

                list.Add(group);
            }

            return list;
        }

    }
}
