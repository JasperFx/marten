#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Marten.Events.Projections;

/// <summary>
/// This is used to create projections that utilize scoped or transient
/// IoC services during execution
/// </summary>
/// <typeparam name="TProjection"></typeparam>
internal class ScopedProjectionWrapper<TProjection> : IProjection, IProjectionSource
    where TProjection : IProjection
{
    private readonly IServiceProvider _serviceProvider;

    public ScopedProjectionWrapper(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
    {
        using var scope = _serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var projection = sp.GetRequiredService<TProjection>();
        await projection.ApplyAsync(operations, streams, cancellation).ConfigureAwait(false);
    }

    private string _projectionName;

    public string ProjectionName
    {
        get
        {
            if (_projectionName.IsEmpty())
            {
                using var scope = _serviceProvider.CreateScope();
                var sp = scope.ServiceProvider;
                var projection = sp.GetRequiredService<TProjection>();

                if (projection is IProjectionSource s)
                {
                    _projectionName = s.ProjectionName;
                }
                else
                {
                    var wrapper = new ProjectionWrapper(projection, Lifecycle);
                    _projectionName = wrapper.ProjectionName;
                }
            }

            return _projectionName;
        }
        set
        {
            _projectionName = value;
        }
    }

    public ProjectionLifecycle Lifecycle { get; set; }
    public Type ProjectionType { get; init; }

    private AsyncOptions _asyncOptions;
    public AsyncOptions Options
    {

        get
        {
            if (_asyncOptions == null)
            {
                using var scope = _serviceProvider.CreateScope();
                var sp = scope.ServiceProvider;
                var projection = sp.GetRequiredService<TProjection>();

                if (projection is IProjectionSource s)
                {
                    _asyncOptions = s.Options;
                }
                else
                {
                    var wrapper = new ProjectionWrapper(projection, Lifecycle);
                    _asyncOptions = wrapper.Options;
                }
            }

            return _asyncOptions;
        }
    }

    public IEnumerable<Type> PublishedTypes()
    {
        using var scope = _serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var projection = sp.GetRequiredService<TProjection>();

        if (projection is IProjectionSource s)
        {
            return s.PublishedTypes();
        }
        else
        {
            var wrapper = new ProjectionWrapper(projection, Lifecycle);
            return wrapper.PublishedTypes();
        }
    }

    public IReadOnlyList<AsyncProjectionShard> AsyncProjectionShards(DocumentStore store)
    {
        using var scope = _serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var projection = sp.GetRequiredService<TProjection>();

        if (projection is IProjectionSource s)
        {
            var shards = s.AsyncProjectionShards(store);
            if (_projectionName.IsNotEmpty())
            {
                foreach (var shard in shards)
                {
                    shard.OverrideProjectionName(_projectionName);
                }
            }

            return shards;
        }
        else
        {
            var wrapper = (IProjectionSource)new ProjectionWrapper(projection, Lifecycle){ProjectionName = _projectionName};
            return wrapper.AsyncProjectionShards(store);
        }
    }

    public ValueTask<EventRangeGroup> GroupEvents(DocumentStore store, IMartenDatabase daemonDatabase, EventRange range,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public IProjection Build(DocumentStore store)
    {
        return this;
    }

    public uint ProjectionVersion { get; set; }

    public bool TryBuildReplayExecutor(DocumentStore store, IMartenDatabase database, out IReplayExecutor executor)
    {
        // TODO -- this might still be possible
        executor = default;
        return false;
    }
}
