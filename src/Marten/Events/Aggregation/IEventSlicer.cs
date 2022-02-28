using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Storage;
#nullable enable
namespace Marten.Events.Aggregation
{
    public interface IEventSlicer<TDoc, TId>
    {
        /// <summary>
        /// This is called by inline projections
        /// </summary>
        /// <param name="querySession"></param>
        /// <param name="streams"></param>
        /// <returns></returns>
        ValueTask<IReadOnlyList<EventSlice<TDoc, TId>>> SliceInlineActions(IQuerySession querySession,
            IEnumerable<StreamAction> streams);

        /// <summary>
        /// This is called by the asynchronous projection runner
        /// </summary>
        /// <param name="querySession"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> SliceAsyncEvents(IQuerySession querySession,
            List<IEvent> events);
    }
}
