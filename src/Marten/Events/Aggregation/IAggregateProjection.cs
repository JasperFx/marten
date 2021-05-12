using System;
using System.Collections.Generic;
using Marten.Events.Projections;
#nullable enable
namespace Marten.Events.Aggregation
{
    /// <summary>
    /// Internal service within aggregating projections
    /// </summary>
    public interface IAggregateProjection // THIS NEEDS TO REMAIN PUBLIC
    {
        Type AggregateType { get; }

        string ProjectionName { get; }

        bool MatchesAnyDeleteType(StreamAction action);
        bool MatchesAnyDeleteType(IEventSlice slice);

        Type[] AllEventTypes { get; }
        bool AppliesTo(IEnumerable<Type> eventTypes);
    }
}
