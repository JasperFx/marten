using System;
using System.Collections.Generic;
using Marten.Events.Projections;

namespace Marten.Events.Aggregation
{
    public interface IAggregateProjection
    {
        Type AggregateType { get; }

        string ProjectionName { get; }

        bool MatchesAnyDeleteType(StreamAction action);
        bool MatchesAnyDeleteType(IEventSlice slice);

        Type[] AllEventTypes { get; }
        bool AppliesTo(IEnumerable<Type> eventTypes);
    }
}
