using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon;
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
    }

    public string ProjectionName { get; set; }
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
        return new List<AsyncProjectionShard> { new(this, new ISqlFragment[0]) };
    }

    public ValueTask<EventRangeGroup> GroupEvents(DocumentStore store, IMartenDatabase daemonDatabase, EventRange range,
        CancellationToken cancellationToken)
    {
        return new ValueTask<EventRangeGroup>(new TenantedEventRangeGroup(store, daemonDatabase, _projection, range,
            cancellationToken));
    }
}
