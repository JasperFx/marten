using System;
using System.Collections.Generic;
using LamarCodeGeneration;

namespace Marten.Events.V4Concept.Aggregation
{
    public interface IAggregateProjection
    {
        Type AggregateType { get; }

        bool MatchesAnyDeleteType(StreamAction action);
        bool MatchesAnyDeleteType(IStreamFragment action);

        string ProjectionName { get; }
    }
}
