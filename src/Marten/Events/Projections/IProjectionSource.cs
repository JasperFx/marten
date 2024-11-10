using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Weasel.Core;

namespace Marten.Events.Projections;

/// <summary>
/// Interface for sources of projections
/// Sources of projections are used to define the behavior how a projection is built for a given projection type
/// Optimized for async usage
/// </summary>
public interface IProjectionSource: IReadOnlyProjectionData
{
    AsyncOptions Options { get; }

    /// <summary>
    ///     This is *only* a hint to Marten about what projected document types
    ///     are published by this projection to aid the "generate ahead" model
    /// </summary>
    /// <returns></returns>
    IEnumerable<Type> PublishedTypes();

    IReadOnlyList<AsyncProjectionShard> AsyncProjectionShards(DocumentStore store);

    ValueTask<EventRangeGroup> GroupEvents(DocumentStore store, IMartenDatabase daemonDatabase,
        EventRange range,
        CancellationToken cancellationToken);

    IProjection Build(DocumentStore store);

    ISubscriptionExecution BuildExecution(AsyncProjectionShard shard, DocumentStore store, IMartenDatabase database,
        ILogger logger);

    /// <summary>
    /// Specify that this projection is a non 1 version of the original projection definition to opt
    /// into Marten's parallel blue/green deployment of this projection.
    /// </summary>
    public uint ProjectionVersion { get; set; }

    bool TryBuildReplayExecutor(DocumentStore store, IMartenDatabase database, out IReplayExecutor executor);
}

/// <summary>
///     Optional interface to expose additional schema objects to be
///     built as part of the event store
/// </summary>
public interface IProjectionSchemaSource
{
    IEnumerable<ISchemaObject> CreateSchemaObjects(EventGraph events);
}


