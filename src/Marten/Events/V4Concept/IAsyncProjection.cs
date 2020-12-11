using System.Collections.Generic;
using Marten.Events.V4Concept.Aggregation;
using Marten.Linq.SqlGeneration;

namespace Marten.Events.V4Concept
{

    public interface IAsyncProjection
    {
        /// <summary>
        /// For the event fetching, only fetch events that are relevant
        /// to this projection and projection shard
        /// </summary>
        /// <returns></returns>
        ISqlFragment EventFilter();
    }

    public interface IStreamableProjection : IAsyncProjection
    {
        /// <summary>
        /// Starts a new batch of updates with "floor" being
        /// the starting event
        /// </summary>
        /// <param name="floor"></param>
        /// <returns></returns>
        IStreamingAsyncBatch StartBatch(int floor);

    }

    public interface IBatchedProjection : IAsyncProjection
    {
        IAsyncBatch CreateBatch(IEnumerable<IEvent> events);
    }
}
