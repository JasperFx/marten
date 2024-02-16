#nullable enable
using System;
using System.Collections.Generic;
using Marten.Events.Projections;

namespace Marten.Events.Aggregation;

/// <summary>
///     Internal service within aggregating projections
/// </summary>
public interface IAggregateProjection // THIS NEEDS TO REMAIN PUBLIC
{
    Type AggregateType { get; }

    string ProjectionName { get; }

    Type[] AllEventTypes { get; }

    ProjectionLifecycle Lifecycle { get; }

    bool MatchesAnyDeleteType(StreamAction action);
    bool MatchesAnyDeleteType(IEventSlice slice);
    bool AppliesTo(IEnumerable<Type> eventTypes);


    /// <summary>
    /// Specify that this projection is a non 1 version of the original projection definition to opt
    /// into Marten's parallel blue/green deployment of this projection.
    /// </summary>
    uint ProjectionVersion { get; set; }
}
