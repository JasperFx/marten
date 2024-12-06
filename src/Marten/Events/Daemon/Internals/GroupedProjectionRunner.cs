using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Daemon;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Storage;

namespace Marten.Events.Daemon.Internals;

internal class AggregationProjectionRunner<TDoc, TId>: IGroupedProjectionRunner
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

        // TODO -- let this be variable?
        SliceBehavior = SliceBehavior.Preprocess;
    }

    public string ProjectionShardIdentity { get; }

    public string ShardIdentity { get; }
    public string DatabaseIdentifier => _database.Identifier;

    public ValueTask DisposeAsync()
    {
        return new ValueTask();
    }

    public SliceBehavior SliceBehavior { get; }


    public IEventSlicer Slicer { get; }

    public async Task<IProjectionBatch> BuildBatchAsync(EventRange range)
    {
        // TODO -- NEED TO PASS THROUGH A CANCELLATION TOKEN
        var cancellation = CancellationToken.None;
        var batch = new ProjectionUpdateBatch(_store, _database, range.Agent.Mode, cancellation)
        {
            ShouldApplyListeners = range.Agent.Mode == ShardExecutionMode.Continuous && range.Events.Any()
        };

        // Mark the progression
        batch.Queue.Post(range.BuildProgressionOperation(_store.Events));

        var groups = range.Groups.OfType<SliceGroup<TDoc, TId>>().ToArray();
        await Parallel.ForEachAsync(groups, cancellation,
                async (group, _) =>
                    await batch.ProcessAggregationAsync<TDoc, TId>(group, cancellation).ConfigureAwait(false))
            .ConfigureAwait(false);

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


internal class GroupedProjectionRunner: IGroupedProjectionRunner
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

    public SliceBehavior SliceBehavior { get; } = SliceBehavior.Preprocess;

    public async Task<IProjectionBatch> BuildBatchAsync(EventRange range)
    {
        // var batch = new ProjectionUpdateBatch(_store, _database, range.Agent.Mode, range.Cancellation)
        // {
        //     ShouldApplyListeners = range.Agent.Mode == ShardExecutionMode.Continuous && range.Range.Events.Any()
        // };
        //
        // // Mark the progression
        // batch.Queue.Post(range.BuildProgressionOperation(_store.Events));

        throw new NotImplementedException();
        //await range.ConfigureUpdateBatch(batch).ConfigureAwait(false);
        // await batch.WaitForCompletion().ConfigureAwait(false);
        //
        // return batch;
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

    public IEventSlicer Slicer => throw new NotImplementedException();

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
