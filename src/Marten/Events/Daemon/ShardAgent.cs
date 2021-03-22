using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{

    /// <summary>
    /// Responsible for running a single async projection shard at runtime. Equivalent to V3 ProjectionTrack
    /// </summary>
    internal class ShardAgent : IShardAgent, IObserver<ShardState>
    {
        private readonly DocumentStore _store;
        private readonly AsyncProjectionShard _projectionShard;
        private readonly ILogger _logger;
        private CancellationToken _cancellation;
        private TransformBlock<EventRange, EventRangeGroup> _grouping;
        private readonly ProjectionController _controller;
        private ActionBlock<Command> _commandBlock;
        private TransformBlock<EventRange, EventRange> _loader;
        private EventFetcher _fetcher;
        private ShardStateTracker _tracker;
        private IDisposable _subscription;
        private ProjectionDaemon _daemon;
        private CancellationTokenSource _cancellationSource;
        private ActionBlock<EventRangeGroup> _building;
        private readonly ProjectionSource _source;
        private bool _isStopping = false;

        public ShardAgent(DocumentStore store, AsyncProjectionShard projectionShard, ILogger logger, CancellationToken cancellation)
        {
            if (cancellation == CancellationToken.None)
            {
                _cancellationSource = new CancellationTokenSource();
                _cancellation = _cancellationSource.Token;
            }

            Name = projectionShard.Name;

            _store = store;
            _projectionShard = projectionShard;
            _logger = logger;
            _cancellation = cancellation;

            _source = projectionShard.Source;

            _controller =
                new ProjectionController(projectionShard.Name, this, projectionShard.Source.Options);
        }

        public ShardName Name { get; }


        private async Task<EventRange> loadEvents(EventRange range)
        {
            await _daemon.TryAction(this, async () =>
            {
                await _fetcher.Load(_projectionShard.Name, range, _cancellation);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Loaded events for {Range}", range);
                }
            }, _cancellation, logException:(logger, e) =>
            {
                logger.LogError(e, "Error loading events for {Range}", range);
            });

            return range;
        }

        private void processCommand(Command command) => command.Apply(_controller);

        public AgentStatus Status { get; private set; }


        public void StartRange(EventRange range)
        {
            if (_cancellation.IsCancellationRequested) return;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Enqueued processing of {Range}", range);
            }

            _loader.Post(range);
        }

        public Task TryAction(Func<Task> action, CancellationToken token, Action<ILogger, Exception> logException = null, EventRangeGroup group = null)
        {
            return _daemon.TryAction(this, action, token, logException:logException, group:group);
        }

        public bool IsStopping()
        {
            return _isStopping;
        }

        public async Task<long> Start(ProjectionDaemon daemon)
        {
            _logger.LogInformation("Starting projection agent for '{ShardName}'", _projectionShard.Name);

            var singleFileOptions = new ExecutionDataflowBlockOptions
            {
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1,
                CancellationToken = _cancellation,
            };

            _commandBlock = new ActionBlock<Command>(processCommand, singleFileOptions);
            _loader = new TransformBlock<EventRange, EventRange>(loadEvents, singleFileOptions);

            _tracker = daemon.Tracker;
            _daemon = daemon;


            _fetcher = new EventFetcher(_store, _projectionShard.EventFilters);
            _grouping = new TransformBlock<EventRange, EventRangeGroup>(groupEventRange, singleFileOptions);


            _building = new ActionBlock<EventRangeGroup>(processRange, singleFileOptions);

            _grouping.LinkTo(_building);
            _loader.LinkTo(_grouping, e => e.Events.Any());

            var lastCommitted = await _store.Advanced.ProjectionProgressFor(_projectionShard.Name, _cancellation);

            foreach (var storageType in _source.Options.StorageTypes)
            {
                // TODO -- this will have to get fancier when we do multi-tenancy by database
                _store.Tenancy.Default.EnsureStorageExists(storageType);
            }

            _commandBlock.Post(Command.Started(_tracker.HighWaterMark, lastCommitted));

            _subscription = _tracker.Subscribe(this);

            _logger.LogInformation("Projection agent for '{ShardName}' has started from sequence {LastCommitted} and a high water mark of {HighWaterMark}", _projectionShard.Name, lastCommitted, _tracker.HighWaterMark);

            Status = AgentStatus.Running;

            Position = lastCommitted;
            return lastCommitted;
        }

        private async Task processRange(EventRangeGroup group)
        {
            if (_cancellation.IsCancellationRequested) return;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Shard '{ShardName}': Starting to process events for {Group}", Name, group);
            }

            ProjectionUpdateBatch batch = null;

            // Building the ProjectionUpdateBatch
            await TryAction(async () =>
            {
                batch = await buildUpdateBatch(@group);
                group.Dispose();
            }, group.Cancellation, (logger, e) =>
            {
                logger.LogError(e, "Failure while trying to process updates for event range {EventRange} for projection shard '{ShardName}'", group, Name);
            }, group:group);

            // Executing the SQL commands for the ProjectionUpdateBatch
            await TryAction(async () =>
            {
                await ExecuteBatch(batch);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Shard '{ShardName}': Configured batch {Group}", Name, group);
                }
            }, _cancellation, (logger, e) =>
            {
                logger.LogError(e, "Failure while trying to process updates for event range {EventRange} for projection shard '{ShardName}'", group, Name);
            });
        }

        private async Task<ProjectionUpdateBatch> buildUpdateBatch(EventRangeGroup @group)
        {
            if (group.Cancellation.IsCancellationRequested) return null; // get out of here early instead of letting it linger

            group.Reset();
            using var batch = StartNewBatch(group);

            await group.ConfigureUpdateBatch(this, batch);

            if (group.Cancellation.IsCancellationRequested) return batch; // get out of here early instead of letting it linger

            batch.Queue.Complete();
            await batch.Queue.Completion;

            return batch;
        }

        private async Task<EventRangeGroup> groupEventRange(EventRange range)
        {
            if (_cancellation.IsCancellationRequested) return null;

            EventRangeGroup group = null;

            await TryAction(() =>
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Shard '{ShardName}':Starting to group {Range}", Name, range);
                }

                group = _source.GroupEvents(_store, range, _cancellation);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Shard '{ShardName}': successfully grouped {Range}", Name, range);
                }

                return Task.CompletedTask;

            }, _cancellation, (logger, e) =>
            {
                logger.LogError(e, "Error while trying to group event range {EventRange} for projection shard {ShardName}", range, Name);
            });



            return group;
        }

        public Task Stop(Exception ex = null)
        {
            _isStopping = true;

            _logger.LogInformation("Stopping projection shard '{ShardName}'", _projectionShard.Name);

            _cancellationSource?.Cancel();

            _commandBlock.Complete();
            _loader.Complete();
            _grouping.Complete();
            _building.Complete();

            _subscription.Dispose();

            _fetcher.Dispose();

            _subscription = null;
            _fetcher = null;
            _commandBlock = null;
            _grouping = null;
            _loader = null;
            _building = null;

            _logger.LogInformation("Stopped projection shard '{ShardName}'", _projectionShard.Name);

            _tracker.Publish(new ShardState(_projectionShard.Name, Position)
            {
                Action = ShardAction.Stopped,
                Exception = ex
            });

            _isStopping = false;

            return Task.CompletedTask;
        }

        public async Task Pause(TimeSpan timeout)
        {
            await Stop();

            Status = AgentStatus.Paused;
            _tracker.Publish(new ShardState(_projectionShard, ShardAction.Paused));

#pragma warning disable 4014
            // ReSharper disable once MethodSupportsCancellation
            Task.Run(async () =>
#pragma warning restore 4014
            {
                // ReSharper disable once MethodSupportsCancellation
                await Task.Delay(timeout);
                _cancellationSource = new CancellationTokenSource();
                _cancellation = _cancellationSource.Token;

                await _daemon.TryAction(this, async () =>
                {
                    try
                    {
                        await Start(_daemon);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Error trying to start shard '{ShardName}' after pausing", _projectionShard.Name);
                        throw new ShardStartException(_projectionShard.Name, e);
                    }
                }, _cancellation);


            });
        }


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
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Projection Shard '{ShardName}' received high water mark at {Sequence}", ShardName, value.Sequence);
                }

                _commandBlock.Post(
                    Command.HighWaterMarkUpdated(value.Sequence));
            }
        }

        public ShardName ShardName => _projectionShard.Name;

        public ProjectionUpdateBatch StartNewBatch(EventRangeGroup group)
        {
            var session = _store.LightweightSession();
            return new ProjectionUpdateBatch(_store.Events, (DocumentSessionBase) session, group.Range, group.Cancellation);
        }

        public async Task ExecuteBatch(ProjectionUpdateBatch batch)
        {
            if (_cancellation.IsCancellationRequested) return;

            await batch.Queue.Completion;

            if (_cancellation.IsCancellationRequested) return;

            await using (var session = (DocumentSessionBase)_store.LightweightSession())
            {
                try
                {
                    await session.ExecuteBatchAsync(batch, _cancellation);

                    _logger.LogInformation("Shard '{ShardName}': Executed updates for {Range}", ShardName, batch.Range);
                }
                catch (Exception e)
                {
                    if (!_cancellation.IsCancellationRequested)
                    {
                        _logger.LogError(e, "Failure in shard '{ShardName}' trying to execute an update batch for {Range}", ShardName, batch.Range);
                        throw;
                    }
                }
            }

            batch.Dispose();

            if (_cancellation.IsCancellationRequested) return;

            Position = batch.Range.SequenceCeiling;

            _tracker.Publish(new ShardState(ShardName, batch.Range.SequenceCeiling));

            _commandBlock.Post(Command.Completed(batch.Range));
        }

        public long Position { get; private set; }





    }
}
