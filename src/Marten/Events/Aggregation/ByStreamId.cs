using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Events.Projections;
using Marten.Storage;
#nullable enable
namespace Marten.Events.Aggregation
{
    internal class ByStreamId<TDoc>: IEventSlicer<TDoc, Guid>
    {
        public IReadOnlyList<EventSlice<TDoc, Guid>> Slice(IEnumerable<StreamAction> streams, ITenancy tenancy)
        {
            return streams.Select(s =>
            {
                var tenant = tenancy[s.TenantId];
                return new EventSlice<TDoc, Guid>(s.Id, tenant, s.Events);
            }).ToList();
        }


        public IReadOnlyList<TenantSliceGroup<TDoc, Guid>> Slice(IReadOnlyList<IEvent> events, ITenancy tenancy)
        {
            var list = new List<TenantSliceGroup<TDoc, Guid>>();
            var byTenant = events.GroupBy(x => x.TenantId);

            foreach (var tenantGroup in byTenant)
            {
                var tenant = tenancy[tenantGroup.Key];

                var slices = tenantGroup
                    .GroupBy(x => x.StreamId)
                    .Select(x => new EventSlice<TDoc, Guid>(x.Key, tenant, x));

                var group = new TenantSliceGroup<TDoc, Guid>(tenant, slices);

                list.Add(group);
            }

            return list;
        }
    }
}
