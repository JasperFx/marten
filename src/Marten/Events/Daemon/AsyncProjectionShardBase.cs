using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Linq.SqlGeneration;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{
    internal abstract class AsyncProjectionShardBase<T> : IAsyncProjectionShard where T : EventRangeGroup
    {
        private IProjectionAgent _agent;
        private ILogger _logger;
        private CancellationToken _cancellation;
        private TransformBlock<EventRange,T> _slicing;
        private ActionBlock<T> _building;

        protected AsyncProjectionShardBase(ShardName identifier, ISqlFragment[] eventFilters, DocumentStore store, AsyncOptions options)
        {
            Store = store;
            Name = identifier;
            EventFilters = eventFilters;
            Options = options;
        }

        public CancellationToken Cancellation => _cancellation;

        public DocumentStore Store { get; }

        public ISqlFragment[] EventFilters { get; }
        public ShardName Name { get; }
        public AsyncOptions Options { get; }

        public ITargetBlock<EventRange> Start(IProjectionAgent agent, ILogger logger, CancellationToken token)
        {
            _cancellation = token;
            _agent = agent;
            _logger = logger;

            var singleFileOptions = new ExecutionDataflowBlockOptions
            {
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1,
                CancellationToken = token
            };

            _slicing = new TransformBlock<EventRange, T>(x => groupEventRange(x), singleFileOptions);


            _building = new ActionBlock<T>(processRange, singleFileOptions);

            _slicing.LinkTo(_building);

            ensureStorageExists();

            return _slicing;
        }

        protected virtual void ensureStorageExists()
        {
            // Nothing
        }


        private async Task processRange(T group)
        {
            if (_cancellation.IsCancellationRequested) return;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Shard '{ShardName}': Starting to process events for {Group}", Name, group);
            }

            // TODO -- this can be retried much more granually
            await _agent.TryAction(async () =>
            {
                try
                {


                    if (group.Cancellation.IsCancellationRequested) return;

                    var batch = await buildUpdateBatch(@group);

                    if (group.Cancellation.IsCancellationRequested) return;

                    await _agent.ExecuteBatch(batch);

                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("Shard '{ShardName}': Configured batch {Group}", Name, group);
                    }

                    group.Dispose();
                }
                catch (Exception e)
                {
                    if (!_cancellation.IsCancellationRequested)
                    {
                        _logger.LogError(e, "Failure while trying to process updates for event range {EventRange} for projection shard '{ShardName}'", group, Name);
                        throw;
                    }
                }
            }, _cancellation);
        }

        private async Task<ProjectionUpdateBatch> buildUpdateBatch(T @group)
        {
            group.Reset();
            using var batch = _agent.StartNewBatch(group);

            await group.ConfigureUpdateBatch(_agent, batch);

            if (group.Cancellation.IsCancellationRequested) return batch; // get out of here early instead of letting it linger

            batch.Queue.Complete();
            await batch.Queue.Completion;

            return batch;
        }

        private async Task<T> groupEventRange(EventRange range)
        {
            if (_cancellation.IsCancellationRequested) return null;

            T group = null;

            await _agent.TryAction(() =>
            {
                try
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("Shard '{ShardName}':Starting to slice {Range}", Name, range);
                    }

                    group = applyGrouping(range);
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("Shard '{ShardName}': successfully slice {Range}", Name, range);
                    }

                    return Task.CompletedTask;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error while trying to group event range {EventRange} for projection shard {ShardName}", range, Name);
                    throw;
                }
            }, _cancellation);



            return group;
        }

        protected abstract T applyGrouping(EventRange range);

        public Task Stop()
        {
            if (_slicing == null) return Task.CompletedTask;

            _slicing.Complete();
            _building.Complete();

            _logger?.LogInformation("Shard '{ShardName}': Stopped", Name.Identity);

            _slicing = null;
            _building = null;

            return Task.CompletedTask;
        }
    }
}
