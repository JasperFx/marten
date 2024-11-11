using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Daemon;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;

namespace Marten.Events.Daemon.Internals;

public abstract class EventRangeGroup: EventRangeGroup<ProjectionUpdateBatch>
{
    protected EventRangeGroup(EventRange range, CancellationToken parent) : base(range)
    {
    }
}

internal class AggregationProjectionRunner<TDoc, TId>: IAggregationProjectionRunner<TDoc, TId>
{
    private readonly DocumentStore _store;
    private readonly IMartenDatabase _database;
    private readonly IEventSlicer<TDoc, TId> _slicer;
    private readonly IProjectionSource _source;

    // TODO -- try to at least sometimes use IEventSlicer
    public AggregationProjectionRunner(AsyncProjectionShard shard, DocumentStore store, IMartenDatabase database,
        IEventSlicer<TDoc, TId> slicer)
    {
        _store = store;
        _database = database;
        _slicer = slicer;
        _source = shard.Source;

        ShardIdentity = shard.Name.Identity;
        ProjectionShardIdentity = shard.Name.Identity;
        if (database.Identifier != "Marten")
        {
            ProjectionShardIdentity += $"@{database.Identifier}";
        }
    }

    public string ProjectionShardIdentity { get; }

    public string ShardIdentity { get; }
    public string DatabaseIdentifier => _database.Identifier;

    public ValueTask DisposeAsync()
    {
        return new ValueTask();
    }

    public async Task<IAggregation> BuildBatchAsync(TenantedSliceGroup<TDoc, TId> group)
    {
        var batch = new ProjectionUpdateBatch(_store, _database, group.Agent.Mode, group.Cancellation)
        {
            ShouldApplyListeners = group.Agent.Mode == ShardExecutionMode.Continuous && group.Range.Events.Any()
        };

        // Mark the progression
        batch.Queue.Post(group.Range.BuildProgressionOperation(_store.Events));

        await group.ConfigureUpdateBatch(batch).ConfigureAwait(false);
        await batch.WaitForCompletion().ConfigureAwait(false);

        return batch;
    }

    public bool TryBuildReplayExecutor(out IReplayExecutor executor)
    {
        if (_store.Events.UseOptimizedProjectionRebuilds &&
            _source.TryBuildReplayExecutor(_store, _database, out executor))
        {
            return true;
        }

        executor = default;
        return false;
    }

    public async ValueTask<TenantedSliceGroup<TDoc, TId>> GroupEvents(EventRange range, CancellationToken cancellationToken)
    {
        var groups = await  _slicer.SliceAsyncEvents(range.Events).ConfigureAwait(false);
        return new TenantedSliceGroup<TDoc, TId>(range, _slicer, groups);
    }

    public IAggregation Database { get; }

    public async Task EnsureStorageExists(CancellationToken token)
    {
        if (_store.Options.AutoCreateSchemaObjects == AutoCreate.None)
        {
            return;
        }

        foreach (var storageType in _source.Options.StorageTypes)
            await _database.EnsureStorageExistsAsync(storageType, token).ConfigureAwait(false);

        foreach (var publishedType in _source.PublishedTypes())
            await _database.EnsureStorageExistsAsync(publishedType, token).ConfigureAwait(false);
    }

    public ErrorHandlingOptions ErrorHandlingOptions(ShardExecutionMode mode)
    {
        return mode == ShardExecutionMode.Continuous
            ? _store.Options.Projections.Errors
            : _store.Options.Projections.RebuildErrors;
    }
}


internal class GroupedProjectionRunner: IGroupedProjectionRunner<ProjectionUpdateBatch, EventRangeGroup>
{
    private readonly DocumentStore _store;
    private readonly IMartenDatabase _database;
    private readonly IProjectionSource _source;

    public GroupedProjectionRunner(AsyncProjectionShard shard, DocumentStore store, IMartenDatabase database)
    {
        _store = store;
        _database = database;
        _source = shard.Source;

        ShardIdentity = shard.Name.Identity;
        ProjectionShardIdentity = shard.Name.Identity;
        if (database.Identifier != "Marten")
        {
            ProjectionShardIdentity += $"@{database.Identifier}";
        }
    }

    public IMartenDatabase Database => _database;

    public string ProjectionShardIdentity { get; }

    public string ShardIdentity { get; }
    public string DatabaseIdentifier => _database.Identifier;

    public ValueTask DisposeAsync()
    {
        return new ValueTask();
    }

    public async Task<ProjectionUpdateBatch> BuildBatchAsync(EventRangeGroup group)
    {
        var batch = new ProjectionUpdateBatch(_store, _database, group.Agent.Mode, group.Cancellation)
        {
            ShouldApplyListeners = group.Agent.Mode == ShardExecutionMode.Continuous && group.Range.Events.Any()
        };

        // Mark the progression
        batch.Queue.Post(group.Range.BuildProgressionOperation(_store.Events));

        await group.ConfigureUpdateBatch(batch).ConfigureAwait(false);
        await batch.WaitForCompletion().ConfigureAwait(false);

        return batch;
    }

    public bool TryBuildReplayExecutor(out IReplayExecutor executor)
    {
        if (_store.Events.UseOptimizedProjectionRebuilds &&
            _source.TryBuildReplayExecutor(_store, _database, out executor))
        {
            return true;
        }

        executor = default;
        return false;
    }

    public ValueTask<EventRangeGroup> GroupEvents(EventRange range, CancellationToken cancellationToken)
    {
        return _source.GroupEvents(_store, _database, range, cancellationToken);
    }

    public async Task EnsureStorageExists(CancellationToken token)
    {
        if (_store.Options.AutoCreateSchemaObjects == AutoCreate.None)
        {
            return;
        }

        foreach (var storageType in _source.Options.StorageTypes)
            await _database.EnsureStorageExistsAsync(storageType, token).ConfigureAwait(false);

        foreach (var publishedType in _source.PublishedTypes())
            await _database.EnsureStorageExistsAsync(publishedType, token).ConfigureAwait(false);
    }

    public ErrorHandlingOptions ErrorHandlingOptions(ShardExecutionMode mode)
    {
        return mode == ShardExecutionMode.Continuous
            ? _store.Options.Projections.Errors
            : _store.Options.Projections.RebuildErrors;
    }
}
