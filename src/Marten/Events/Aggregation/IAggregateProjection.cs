#nullable enable
using System;
using System.Collections.Generic;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Schema;

namespace Marten.Events.Aggregation;

/// <summary>
///     Internal service within aggregating projections
/// </summary>
public interface IAggregateProjection // THIS NEEDS TO REMAIN PUBLIC
{
    Type AggregateType { get; }

    string ProjectionName { get; }

    Type[] AllEventTypes { get; }

    ProjectionLifecycle Lifecycle { get; set; }

    bool MatchesAnyDeleteType(StreamAction action);
    bool MatchesAnyDeleteType(IEventSlice slice);
    bool AppliesTo(IEnumerable<Type> eventTypes);

    AsyncOptions Options { get; }


    /// <summary>
    /// Specify that this projection is a non 1 version of the original projection definition to opt
    /// into Marten's parallel blue/green deployment of this projection.
    /// </summary>
    uint ProjectionVersion { get; set; }

    object ApplyMetadata(object aggregate, IEvent lastEvent);

    /// <summary>
    /// Apply any necessary configuration to the document mapping to work with the projection and append
    /// mode
    /// </summary>
    /// <param name="mapping"></param>
    /// <param name="storeOptions"></param>
    void ConfigureAggregateMapping(DocumentMapping mapping, StoreOptions storeOptions);
}
