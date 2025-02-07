using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Descriptions;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Storage;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Projections;

internal class ProjectionWrapper: IProjectionSource
{
    private readonly IProjection _projection;

    public ProjectionWrapper(IProjection projection, ProjectionLifecycle lifecycle)
    {
        _projection = projection;
        Lifecycle = lifecycle;
        ProjectionName = projection.GetType().FullNameInCode();

        Inner = _projection;
    }

    public SubscriptionDescriptor Describe()
    {
        return new SubscriptionDescriptor(this, SubscriptionType.EventProjection);
    }

    [ChildDescription]
    public IProjection Inner { get; }

    public string ProjectionName { get; set; }

    [ChildDescription]
    public AsyncOptions Options { get; } = new();

    public IEnumerable<Type> PublishedTypes()
    {
        // Really indeterminate
        yield break;
    }

    public ProjectionLifecycle Lifecycle { get; set; }


    public Type ProjectionType => _projection.GetType();

    IProjection IProjectionSource.Build(DocumentStore store)
    {
        return _projection;
    }

    IReadOnlyList<AsyncProjectionShard> IProjectionSource.AsyncProjectionShards(DocumentStore store)
    {
        if(_projection is EventFilterable filterableProjection)
        {
            return new List<AsyncProjectionShard> { new (this)
            {
                EventTypes = filterableProjection.IncludedEventTypes,
                StreamType = filterableProjection.StreamType,
                IncludeArchivedEvents = filterableProjection.IncludeArchivedEvents
            } };
        }

        return new List<AsyncProjectionShard> { new(this)
        {
            EventTypes = ArraySegment<Type>.Empty,
            StreamType = null,
            IncludeArchivedEvents = false
        } };
    }

    public ValueTask<EventRangeGroup> GroupEvents(DocumentStore store, IMartenDatabase daemonDatabase, EventRange range,
        CancellationToken cancellationToken)
    {
        return new ValueTask<EventRangeGroup>(
            new TenantedEventRangeGroup(
                store,
                daemonDatabase,
                _projection,
                Options,
                range,
                cancellationToken
            )
        );
    }

    /// <summary>
    /// Specify that this projection is a non 1 version of the original projection definition to opt
    /// into Marten's parallel blue/green deployment of this projection.
    /// </summary>
    public uint ProjectionVersion { get; set; } = 1;

    public bool TryBuildReplayExecutor(DocumentStore store, IMartenDatabase database, out IReplayExecutor executor)
    {
        executor = default;
        return false;
    }
}
