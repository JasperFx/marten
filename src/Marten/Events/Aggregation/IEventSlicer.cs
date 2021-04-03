using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Storage;
#nullable enable
namespace Marten.Events.Aggregation
{
    public interface IEventSlicer<TDoc, TId>
    {
        ValueTask<IReadOnlyList<EventSlice<TDoc, TId>>> Slice(IQuerySession querySession, IEnumerable<StreamAction> streams, ITenancy tenancy);
        ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> Slice(IQuerySession querySession, IReadOnlyList<IEvent> events, ITenancy tenancy);
    }
}
