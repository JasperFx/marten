using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Storage;

namespace Marten.Events.V4Concept.Aggregation
{

    public interface IEventSlicer<TDoc, TId>
    {
        IReadOnlyList<EventSlice<TDoc, TId>> Slice(IEnumerable<StreamAction> streams, ITenancy tenancy);
        Task<IReadOnlyList<EventSlice<TDoc, TId>>> Slice(IAsyncEnumerable<IEvent> events, ITenancy tenancy);
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

        public Task<IReadOnlyList<EventSlice<TDoc, Guid>>> Slice(IAsyncEnumerable<IEvent> events, ITenancy tenancy)
        {
            throw new NotImplementedException();
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

        public Task<IReadOnlyList<EventSlice<TDoc, string>>> Slice(IAsyncEnumerable<IEvent> events, ITenancy tenancy)
        {
            throw new NotImplementedException();
        }
    }

}
