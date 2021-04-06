using System.Collections.Generic;
using System.Linq;
using Marten.Events.Projections;
using Marten.Storage;
#nullable enable
namespace Marten.Events.Aggregation
{
    public class ByStreamKey<TDoc>: IEventSlicer<TDoc, string>
    {
        public IReadOnlyList<EventSlice<TDoc, string>> Slice(IEnumerable<StreamAction> streams, ITenancy tenancy)
        {
            return streams.Select(s =>
            {
                var tenant = tenancy[s.TenantId];
                return new EventSlice<TDoc, string>(s.Key!, tenant, s.Events);
            }).ToList();
        }

        public IReadOnlyList<TenantSliceGroup<TDoc, string>> Slice(IReadOnlyList<IEvent> events, ITenancy tenancy)
        {
            var list = new List<TenantSliceGroup<TDoc, string>>();
            var byTenant = events.GroupBy(x => x.TenantId);

            foreach (var tenantGroup in byTenant)
            {
                var tenant = tenancy[tenantGroup.Key];

                var slices = tenantGroup
                    .GroupBy(x => x.StreamKey)
                    .Select(x => new EventSlice<TDoc, string>(x.Key, tenant, x));

                var group = new TenantSliceGroup<TDoc, string>(tenant, slices);

                list.Add(group);
            }

            return list;
        }

    }
}
