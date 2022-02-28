using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Services;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{

    /// <summary>
    /// Responsible for running a single async projection shard at runtime. Equivalent to V3 ProjectionTrack
    /// </summary>
    internal class ShardAgent : IShardAgent, IObserver<ShardState>
    {
        private SessionOptions _sessionOptions;

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

        public CancellationToken Cancellation => _cancellation;


        private async Task<EventRange> loadEvents(EventRange range)
        {
            var parameters = new ActionParameters(this, async () =>
            {
                await _fetcher.Load(_projectionShard.Name, range, _cancellation).ConfigureAwait(false);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Loaded events for {Range}", range);
                }
            })
            {
                LogAction = (logger, e) =>
                {
                    logger.LogError(e, "Error loading events for {Range}", range);
                }
            };


            await _daemon.TryAction(parameters).ConfigureAwait(false);

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

        public Task TryAction(Func<Task> action, CancellationToken token, Action<ILogger, Exception> logException = null, EventRangeGroup group = null, GroupActionMode actionMode = GroupActionMode.Parent)
        {
            var parameters = new ActionParameters(this, action, token == default ? _cancellation : token)
            {
                GroupActionMode = actionMode
            };

            parameters.LogAction = logException ?? parameters.LogAction;
            parameters.Group = group;

            return _daemon.TryAction(parameters);
        }

        public bool IsStopping()
        {
            return _isStopping;
        }

        public async Task<long> Start(ProjectionDaemon daemon)
        {
            _logger.LogInformation("Starting projection agent for '{ShardName}'", _projectionShard.Name);

            _sessionOptions = new SessionOptions
            {
                Tenant = daemon.Tenant, AllowAnyTenant = true, Tracking = DocumentTracking.None
            };

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


            _fetcher = new EventFetcher(_store, _daemon.Tenant, _projectionShard.EventFilters);
            _grouping = new TransformBlock<EventRange, EventRangeGroup>(groupEventRange, singleFileOptions);


            _building = new ActionBlock<EventRangeGroup>(processRange, singleFileOptions);

            _grouping.LinkTo(_building);

            // The filter is important. You may need to allow an empty page to go through
            // just to keep tracking correct
            _loader.LinkTo(_grouping, e => e.Events != null);

            // TODO -- THIS WILL NOT WORK WITH MULTI-TENANTED DATABASES!!!!
            var lastCommitted = await _store.Advanced.ProjectionProgressFor(_projectionShard.Name, token:_cancellation).ConfigureAwait(false);

            foreach (var storageType in _source.Options.StorageTypes)
            {
                await daemon.Tenant.Database.EnsureStorageExistsAsync(storageType, _cancellation).ConfigureAwait(false);
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

            // This should be done *once* here before going to the TryAction()
            group.Reset();

            ProjectionUpdateBatch batch = null;

            // Building the ProjectionUpdateBatch
            await TryAction(async () =>
            {
                batch = await buildUpdateBatch(@group).ConfigureAwait(false);

                @group.Dispose();
            }, _cancellation, (logger, e) =>
            {
                logger.LogError(e, "Failure while trying to process updates for event range {EventRange} for projection shard '{ShardName}'", @group, Name);
            }, @group:@group).ConfigureAwait(false);

            // This has failed, so get out of here.
            if (batch == null) return;

            // Executing the SQL commands for the ProjectionUpdateBatch
            await TryAction(async () =>
            {
                await ExecuteBatch(batch).ConfigureAwait(false);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Shard '{ShardName}': Successfully processed batch {Group}", Name, @group);
                }
            }, _cancellation, (logger, e) =>
            {
                logger.LogError(e, "Failure while trying to process updates for event range {EventRange} for projection shard '{ShardName}'", @group, Name);
            }).ConfigureAwait(false);
        }

        private async Task<ProjectionUpdateBatch> buildUpdateBatch(EventRangeGroup @group)
        {
            if (group.Cancellation.IsCancellationRequested) return null; // get out of here early instead of letting it linger

            var batch = StartNewBatch(group);

            try
            {
                await @group.ConfigureUpdateBatch(this, batch, @group).ConfigureAwait(false);

                if (group.Cancellation.IsCancellationRequested)
                {
                    if (group.Exception != null)
                    {
                        ExceptionDispatchInfo.Capture(group.Exception).Throw();
                    }

                    return batch; // get out of here early instead of letting it linger
                }

                batch.Queue.Complete();
                await batch.Queue.Completion.ConfigureAwait(false);

                if (group.Exception != null)
                {
                    ExceptionDispatchInfo.Capture(group.Exception).Throw();
                }
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
            if (_cancellation.IsCancellationRequested) return null;

            EventRangeGroup group = null;

            await TryAction(async () =>
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Shard '{ShardName}':Starting to group {Range}", Name, range);
                }

                @group = await _source.GroupEvents(_store, range, _cancellation).ConfigureAwait(false);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Shard '{ShardName}': successfully grouped {Range}", Name, range);
                }

            }, _cancellation, (logger, e) =>
            {
                logger.LogError(e, "Error while trying to group event range {EventRange} for projection shard {ShardName}", range, Name);
            }).ConfigureAwait(false);



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
            await Stop().ConfigureAwait(false);

            Status = AgentStatus.Paused;
            _tracker.Publish(new ShardState(_projectionShard, ShardAction.Paused));

#pragma warning disable 4014
            // ReSharper disable once MethodSupportsCancellation
            Task.Run(async () =>
#pragma warning restore 4014
            {
                // ReSharper disable once MethodSupportsCancellation
                await Task.Delay(timeout).ConfigureAwait(false);
                _cancellationSource = new CancellationTokenSource();
                _cancellation = _cancellationSource.Token;

                var parameters = new ActionParameters(this, async () =>
                {
                    try
                    {
                        await Start(_daemon).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        throw new ShardStartException(_projectionShard.Name, e);
                    }
                });

                parameters.LogAction = (l, e) =>
                {
                    l.LogError(e, "Error trying to start shard '{ShardName}' after pausing",
                        _projectionShard.Name);
                };

                await _daemon.TryAction(parameters).ConfigureAwait(false);
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
            var session = _store.OpenSession(_sessionOptions);
            return new ProjectionUpdateBatch(_store.Events, (DocumentSessionBase) session, group.Range, group.Cancellation);
        }

        public async Task ExecuteBatch(ProjectionUpdateBatch batch)
        {
            if (_cancellation.IsCancellationRequested || batch == null) return;

            await batch.Queue.Completion.ConfigureAwait(false);

            if (_cancellation.IsCancellationRequested) return;



            var session = (DocumentSessionBase)_store.OpenSession(_sessionOptions);
            await using (session.ConfigureAwait(false))
            {
                try
                {
                    await session.ExecuteBatchAsync(batch, _cancellation).ConfigureAwait(false);

                    _logger.LogInformation("Shard '{ShardName}': Executed updates for {Range}", ShardName, batch.Range);
                }
                catch (Exception e)
                {
                    if (!_cancellation.IsCancellationRequested)
                    {
                        _logger.LogError(e,
                            "Failure in shard '{ShardName}' trying to execute an update batch for {Range}", ShardName,
                            batch.Range);
                        throw;
                    }
                }
                finally
                {
                    batch.Dispose();
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
