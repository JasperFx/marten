using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Storage;
#nullable enable
namespace Marten.Events.Aggregation
{
    internal class ByStreamId<TDoc>: IEventSlicer<TDoc, Guid>
    {
        public ValueTask<IReadOnlyList<EventSlice<TDoc, Guid>>> SliceInlineActions(IQuerySession querySession, IEnumerable<StreamAction> streams, ITenancy tenancy)
        {
            return new (streams.Select(s =>
            {
                var tenant = tenancy.GetTenant(s.TenantId);
                return new EventSlice<TDoc, Guid>(s.Id, tenant, s.Events);
            }).ToList());
        }


        public ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, Guid>>> SliceAsyncEvents(IQuerySession querySession,
            List<IEvent> events, ITenancy tenancy)
        {
            var list = new List<TenantSliceGroup<TDoc, Guid>>();
            var byTenant = events.GroupBy(x => x.TenantId);

            foreach (var tenantGroup in byTenant)
            {
                var tenant = tenancy.GetTenant(tenantGroup.Key);

                var slices = tenantGroup
                    .GroupBy(x => x.StreamId)
                    .Select(x => new EventSlice<TDoc, Guid>(x.Key, tenant, x));

                var group = new TenantSliceGroup<TDoc, Guid>(tenant, slices);

                list.Add(group);
            }

            return new ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, Guid>>>(list);
        }
    }
}
