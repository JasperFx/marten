using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Projections;
using Marten.Linq.SqlGeneration;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{
    internal interface IEventRangeGroup: IDisposable
    {
        EventRange Range { get; }
    }

    internal abstract class AsyncProjectionShardBase<T> : IAsyncProjectionShard where T : class, IEventRangeGroup
    {
        private IProjectionUpdater _updater;
        private ILogger<IProjection> _logger;
        private CancellationToken _token;
        private TransformBlock<EventRange,T> _slicing;
        private ActionBlock<T> _building;

        protected AsyncProjectionShardBase(string projectionOrShardName, ISqlFragment[] eventFilters, DocumentStore store, AsyncOptions options)
        {
            Store = store;
            ProjectionOrShardName = projectionOrShardName;
            EventFilters = eventFilters;
            Options = options;
        }

        public DocumentStore Store { get; }

        public ISqlFragment[] EventFilters { get; }
        public string ProjectionOrShardName { get; }
        public AsyncOptions Options { get; }

        public ITargetBlock<EventRange> Start(IProjectionUpdater updater, ILogger<IProjection> logger, CancellationToken token)
        {
            _token = token;
            _updater = updater;
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

            return _slicing;
        }

        private async Task processRange(T group)
        {
            if (_token.IsCancellationRequested) return;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug($"Shard '{ProjectionOrShardName}': Starting to build an update batch for {group}");
            }

            var batch = _updater.StartNewBatch(group.Range);

            await configureUpdateBatch(batch, group, _token);

            batch.Queue.Complete();
            await batch.Queue.Completion;

            await _updater.ExecuteBatch(batch);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug($"Shard '{ProjectionOrShardName}': Configured batch {group}");
            }

            group.Dispose();
        }

        protected abstract Task configureUpdateBatch(ProjectionUpdateBatch batch, T group, CancellationToken token);

        private T groupEventRange(EventRange range)
        {
            if (_token.IsCancellationRequested) return null;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug($"Shard '{ProjectionOrShardName}': Starting to slice {range}");
            }

            var group = applyGrouping(range);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug($"Shard '{ProjectionOrShardName}': successfully sliced {range}");
            }

            return group;
        }

        protected abstract T applyGrouping(EventRange range);

        public async Task Stop()
        {
            if (_slicing == null) return;

            await _slicing.Completion;
            await _building.Completion;

            _slicing.Complete();
            _building.Complete();

            _logger?.LogInformation($"Shard '{ProjectionOrShardName}': Stopped");
        }
    }
}
