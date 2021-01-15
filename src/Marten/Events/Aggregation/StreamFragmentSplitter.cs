using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Storage;

namespace Marten.Events.Aggregation
{

    public interface IEventSlicer<TDoc, TId>
    {
        IReadOnlyList<EventSlice<TDoc, TId>> Slice(IEnumerable<StreamAction> streams, ITenancy tenancy);
        IReadOnlyList<EventSlice<TDoc, TId>> Slice(IReadOnlyList<IEvent> events, ITenancy tenancy);
    }

    public class ByStreamId<TDoc>: IEventSlicer<TDoc, Guid>
    {
        public IReadOnlyList<EventSlice<TDoc, Guid>> Slice(IEnumerable<StreamAction> streams, ITenancy tenancy)
        {
            return streams.Select(s =>
            {
                var tenant = tenancy[s.TenantId];
                return new EventSlice<TDoc, Guid>(s.Id, tenant, s.Events);
            }).ToList();
        }

        public IReadOnlyList<EventSlice<TDoc, Guid>> Slice(IReadOnlyList<IEvent> events, ITenancy tenancy)
        {
            var list = new List<EventSlice<TDoc, Guid>>();
            var byTenant = events.GroupBy(x => x.TenantId);

            foreach (var tenantGroup in byTenant)
            {
                var tenant = tenancy[tenantGroup.Key];

                var slices = tenantGroup
                    .GroupBy(x => x.StreamId)
                    .Select(x => new EventSlice<TDoc, Guid>(x.Key, tenant, x));

                list.AddRange(slices);
            }

            return list;

        }
    }

    public class ByStreamKey<TDoc>: IEventSlicer<TDoc, string>
    {
        public IReadOnlyList<EventSlice<TDoc, string>> Slice(IEnumerable<StreamAction> streams, ITenancy tenancy)
        {
            return streams.Select(s =>
            {
                var tenant = tenancy[s.TenantId];
                return new EventSlice<TDoc, string>(s.Key, tenant, s.Events);
            }).ToList();
        }

        public IReadOnlyList<EventSlice<TDoc, string>> Slice(IReadOnlyList<IEvent> events, ITenancy tenancy)
        {
            var list = new List<EventSlice<TDoc, string>>();
            var byTenant = events.GroupBy(x => x.TenantId);

            foreach (var tenantGroup in byTenant)
            {
                var tenant = tenancy[tenantGroup.Key];

                var slices = tenantGroup
                    .GroupBy(x => x.StreamKey)
                    .Select(x => new EventSlice<TDoc, string>(x.Key, tenant, x));

                list.AddRange(slices);
            }

            return list;
        }
    }

}
