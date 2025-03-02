#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Schema;

namespace Marten.Events.Aggregation;

#region IAggregateProjectionWithSideEffects

/// <summary>
/// Marks a grouped or aggregated projection as emitting "side effects"
/// during asynchronous projection processing
/// </summary>
/// <typeparam name="T"></typeparam>
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

#endregion

public interface IMartenAggregateProjection
{
    /// <summary>
    /// Apply any necessary configuration to the document mapping to work with the projection and append
    /// mode
    /// </summary>
    /// <param name="mapping"></param>
    /// <param name="storeOptions"></param>
    // TODO -- Move off to a separate interface
    void ConfigureAggregateMapping(DocumentMapping mapping, StoreOptions storeOptions);

}

