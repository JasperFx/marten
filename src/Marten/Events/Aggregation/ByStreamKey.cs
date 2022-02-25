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
        public ValueTask<IReadOnlyList<EventSlice<TDoc, string>>> SliceInlineActions(IQuerySession querySession, IEnumerable<StreamAction> streams, ITenancy tenancy)
        {
            return new(streams.Select(s =>
            {
                var tenant = tenancy.GetTenant(s.TenantId);
                return new EventSlice<TDoc, string>(s.Key!, tenant, s.Events);
            }).ToList());
        }

        public async ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, string>>> SliceAsyncEvents(IQuerySession querySession,
            List<IEvent> events, ITenancy tenancy)
        {
            var list = new List<TenantSliceGroup<TDoc, string>>();
            var byTenant = events.GroupBy(x => x.TenantId);

            foreach (var tenantGroup in byTenant)
            {
                var tenant = await tenancy.GetTenantAsync(tenantGroup.Key).ConfigureAwait(false);

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
