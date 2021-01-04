using System;
using System.Collections.Generic;
using LamarCodeGeneration;
using Marten.Events.V4Concept.CodeGeneration;

namespace Marten.Events.V4Concept.Aggregation
{
    public interface IAggregateProjection
    {
        Type AggregateType { get; }

        bool MatchesAnyDeleteType(StreamAction action);
        bool MatchesAnyDeleteType(IEventSlice slice);

        string ProjectionName { get; }

        IEnumerable<MethodSlot> InvalidMethods();
    }
}
