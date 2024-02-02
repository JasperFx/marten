#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Services;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon;

/// <summary>
///     Responsible for running a single async projection shard at runtime. Equivalent to V3 ProjectionTrack
/// </summary>
internal class ShardAgent: IShardAgent, IObserver<ShardState>
{
    private readonly ProjectionController _controller;
    private readonly AsyncProjectionShard _projectionShard;

    private readonly IProjectionSource _source;

    private readonly DocumentStore _store;
    private ActionBlock<EventRangeGroup>? _building;
    private CancellationToken _cancellation;
    private CancellationTokenSource? _cancellationSource;
    private ActionBlock<Command>? _commandBlock;
    private ProjectionDaemon? _daemon;
    private IEventFetcher? _fetcher;

    private TransformBlock<EventRange, EventRangeGroup>? _grouping;
    private TransformBlock<EventRange, EventRange>? _loader;
    private SessionOptions? _sessionOptions;
    private IDisposable? _subscription;
    private ShardStateTracker? _tracker;

    public ShardAgent(DocumentStore store, AsyncProjectionShard projectionShard, ILogger logger,
        CancellationToken cancellation)
    {
        if (cancellation == CancellationToken.None)
        {
            _cancellationSource = new CancellationTokenSource();
            _cancellation = _cancellationSource.Token;
        }

        Name = projectionShard.Name;

        ProjectionShardIdentity = projectionShard.Name.Identity;

        _store = store;
        _projectionShard = projectionShard;
        Logger = logger;
        _cancellation = cancellation;

        _source = projectionShard.Source;

        _controller =
            new ProjectionController(projectionShard.Name, this, projectionShard.Source.Options);
    }

    public ILogger Logger { get; }

    public string ProjectionShardIdentity { get; private set; }

    public CancellationToken Cancellation => _cancellation;

    public AgentStatus Status { get; private set; }

    public bool IsStopping { get; private set; }

    public ShardName ShardName => _projectionShard.Name;

    public long Position { get; private set; }


    void IObserver<ShardState>.OnCompleted()
    {
        // Nothing
    }

    void IObserver<ShardState>.OnError(Exception error)
    {
        // Nothing
    }

    void IObserver<ShardState>.OnNext(ShardState value)
    {
        if (value.ShardName == ShardState.HighWaterMark)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug("Projection Shard '{ProjectionShardIdentity}' received high water mark at {Sequence}",
                    ProjectionShardIdentity, value.Sequence);
            }

            _commandBlock!.Post(
                Command.HighWaterMarkUpdated(value.Sequence));
        }
    }

    public ShardName Name { get; }


    public void StartRange(EventRange range)
    {
        if (_cancellation.IsCancellationRequested)
        {
            return;
        }

        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug("Enqueued processing {Range} for {ProjectionShardIdentity}", range,
                ProjectionShardIdentity);
        }

        _loader!.Post(range);
    }

    public ShardExecutionMode Mode { get; set; } = ShardExecutionMode.Continuous;


    private async Task<EventRange> loadEvents(EventRange range)
    {
        try
        {
            await _fetcher!.Load(range, _cancellation).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error loading events {Range} for {ProjectionShardIdentity}", range,
                ProjectionShardIdentity);

            throw;
        }

        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug("Loaded events {Range} for {ProjectionShardIdentity}", range, ProjectionShardIdentity);
        }

        return range;
    }

    public async Task<long> Start(ProjectionDaemon daemon, ShardExecutionMode mode)
    {
        Mode = mode;

        if (daemon.Database.Identifier != "Marten")
        {
            ProjectionShardIdentity = $"{ProjectionShardIdentity}@{daemon.Database.Identifier}";
        }

        Logger.LogInformation("Starting projection agent for '{ProjectionShardIdentity}'", ProjectionShardIdentity);

        _sessionOptions = SessionOptions.ForDatabase(daemon.Database);

        initializeDataflowBlocks(daemon);

        var lastCommitted = await daemon.Database.ProjectionProgressFor(_projectionShard.Name, _cancellation)
            .ConfigureAwait(false);

        await ensureStorageExists(daemon).ConfigureAwait(false);

        _subscription = _tracker.Subscribe(this);
        _commandBlock?.Post(Command.Started(_tracker.HighWaterMark, lastCommitted));

        Logger.LogInformation(
            "Projection agent for '{ProjectionShardIdentity}' has started from sequence {LastCommitted} and a high water mark of {HighWaterMark}",
            ProjectionShardIdentity, lastCommitted, _tracker.HighWaterMark);

        Status = AgentStatus.Running;

        Position = lastCommitted;
        return lastCommitted;
    }

    private async Task ensureStorageExists(ProjectionDaemon daemon)
    {
        foreach (var storageType in _source.Options.StorageTypes)
            await daemon.Database.EnsureStorageExistsAsync(storageType, _cancellation).ConfigureAwait(false);

        foreach (var publishedType in _source.PublishedTypes())
            await daemon.Database.EnsureStorageExistsAsync(publishedType, _cancellation).ConfigureAwait(false);
    }

    [MemberNotNull(nameof(_commandBlock), nameof(_loader), nameof(_tracker), nameof(_daemon), nameof(_fetcher),
        nameof(_grouping), nameof(_building))]
    private void initializeDataflowBlocks(ProjectionDaemon daemon)
    {
        var singleFileOptions = new ExecutionDataflowBlockOptions
        {
            EnsureOrdered = true, MaxDegreeOfParallelism = 1, CancellationToken = _cancellation
        };

        _commandBlock = new ActionBlock<Command>(command => command.Apply(_controller), singleFileOptions);
        _loader = new TransformBlock<EventRange, EventRange>(loadEvents, singleFileOptions);

        _tracker = daemon.Tracker;
        _daemon = daemon;

        _fetcher = Mode == ShardExecutionMode.Continuous
            ? new EventFetcher(_store, _daemon.Database, _projectionShard.EventFilters)
            : new RebuildingEventFetcher(_store, this, _daemon.Database, _projectionShard.EventFilters);

        _grouping = new TransformBlock<EventRange, EventRangeGroup>(groupEventRange, singleFileOptions);

        _building = new ActionBlock<EventRangeGroup>(processRange, singleFileOptions);

        _grouping.LinkTo(_building);

        // The filter is important. You may need to allow an empty page to go through
        // just to keep tracking correct
        _loader.LinkTo(_grouping, e => e.Events != null);
    }

    private async Task processRange(EventRangeGroup group)
    {
        if (_cancellation.IsCancellationRequested)
        {
            return;
        }

        // This should be done *once* here before going to the TryAction()
        group.Reset();

        var batch = await buildUpdateBatch(group).ConfigureAwait(false);
        group.Dispose();
        if (batch == null) return;

        // Executing the SQL commands for the ProjectionUpdateBatch
        await ExecuteBatch(batch).ConfigureAwait(false);
    }

    private async Task<ProjectionUpdateBatch?> buildUpdateBatch(EventRangeGroup group)
    {
        if (group.Cancellation.IsCancellationRequested)
        {
            return null; // get out of here early instead of letting it linger
        }

        var batch = StartNewBatch(group);

        try
        {
            await group.ConfigureUpdateBatch(batch).ConfigureAwait(false);

            if (group.Cancellation.IsCancellationRequested)
            {
                return batch; // get out of here early instead of letting it linger
            }

            batch.Queue.Complete();
            await batch.Queue.Completion.ConfigureAwait(false);
        }
        finally
        {
            if (batch != null)
            {
                await batch.CloseSession().ConfigureAwait(false);
            }
        }

        return batch;
    }

    private async Task<EventRangeGroup> groupEventRange(EventRange range)
    {
        if (_cancellation.IsCancellationRequested)
        {
            return null;
        }

        return await _source.GroupEvents(_store, _daemon!.Database, range, _cancellation).ConfigureAwait(false);
    }

    public async Task Stop(Exception? ex = null)
    {
        IsStopping = true;

        if (_cancellationSource != null)
        {
#if NET8_0
            await _cancellationSource.CancelAsync().ConfigureAwait(false);
#else
            _cancellationSource.Cancel();
#endif
        }

        _commandBlock?.Complete();
        _loader?.Complete();
        _grouping?.Complete();
        _building?.Complete();

        _subscription?.Dispose();

        _fetcher?.Dispose();

        _subscription = null;
        _fetcher = null;
        _commandBlock = null;
        _grouping = null;
        _loader = null;
        _building = null;

        Logger.LogInformation("Stopped projection shard '{ProjectionShardIdentity}'", ProjectionShardIdentity);

        _tracker?.Publish(new ShardState(_projectionShard.Name, Position)
        {
            Action = ShardAction.Stopped, Exception = ex
        });

        IsStopping = false;
    }

    public ProjectionUpdateBatch StartNewBatch(EventRangeGroup group)
    {
        var session = _store.LightweightSession(_sessionOptions!);
        return new ProjectionUpdateBatch(_store.Events, _store.Options.Projections, (DocumentSessionBase)session,
            group.Range, group.Cancellation, Mode);
    }

    public async Task ExecuteBatch(ProjectionUpdateBatch batch)
    {
        if (_cancellation.IsCancellationRequested || batch == null)
        {
            return;
        }

        await batch.Queue.Completion.ConfigureAwait(false);

        if (_cancellation.IsCancellationRequested)
        {
            return;
        }

        var session = (DocumentSessionBase)_store.IdentitySession(_sessionOptions!);
        await using (session.ConfigureAwait(false))
        {
            try
            {
                await session.ExecuteBatchAsync(batch, _cancellation).ConfigureAwait(false);

                Logger.LogInformation("Shard '{ProjectionShardIdentity}': Executed updates for {Range}",
                    ProjectionShardIdentity, batch.Range);
            }
            catch (Exception e)
            {
                if (!_cancellation.IsCancellationRequested)
                {
                    Logger.LogError(e,
                        "Failure in shard '{ProjectionShardIdentity}' trying to execute an update batch for {Range}",
                        ProjectionShardIdentity,
                        batch.Range);
                    throw;
                }
            }
            finally
            {
                await batch.DisposeAsync().ConfigureAwait(false);
            }
        }

        await batch.DisposeAsync().ConfigureAwait(false);

        if (_cancellation.IsCancellationRequested)
        {
            return;
        }

        Position = batch.Range.SequenceCeiling;

        _tracker?.Publish(new ShardState(ShardName, batch.Range.SequenceCeiling) { Action = ShardAction.Updated });

        _commandBlock?.Post(Command.Completed(batch.Range));
    }

    public async Task<long> DrainSerializationFailureRecording()
    {
        if (_fetcher is RebuildingEventFetcher rebuild)
        {
            return await rebuild.Complete().ConfigureAwait(false);
        }

        return 0;
    }
}
