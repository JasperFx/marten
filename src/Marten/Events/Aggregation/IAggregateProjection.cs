#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Schema;

namespace Marten.Events.Aggregation;

public interface IAggregateProjectionWithSideEffects<T>
{
    /// <summary>
    /// Use to create "side effects" when running an aggregation (single stream, custom projection, multi-stream)
    /// asynchronously in a continuous mode (i.e., not in rebuilds)
    /// </summary>
    /// <param name="operations"></param>
    /// <param name="slice"></param>
    /// <returns></returns>
    ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<T> slice);

    bool IsSingleStream();
}

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
