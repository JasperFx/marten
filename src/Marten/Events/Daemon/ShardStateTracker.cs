using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Baseline.Dates;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{
    public class ShardStateTracker: IObservable<ShardState>, IDisposable
    {
        private readonly ILogger _logger;
        private ImmutableList<IObserver<ShardState>> _listeners = ImmutableList<IObserver<ShardState>>.Empty;
        private readonly ActionBlock<ShardState> _block;

        public ShardStateTracker(ILogger logger)
        {
            _logger = logger;
            _block = new ActionBlock<ShardState>(publish, new ExecutionDataflowBlockOptions
            {
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        }

        public IDisposable Subscribe(IObserver<ShardState> observer)
        {
            if (!_listeners.Contains(observer)) _listeners = _listeners.Add(observer);

            return new Unsubscriber(this, observer);
        }

        internal void Publish(ShardState state)
        {
            if (state.ShardName == ShardState.HighWaterMark)
            {
                HighWaterMark = state.Sequence;
            }

            _block.Post(state);
        }

        public long HighWaterMark { get; private set; }

        internal void MarkHighWater(long sequence)
        {
            Publish(new ShardState(ShardState.HighWaterMark, sequence));
        }

        public Task<ShardState> WaitForShardState(ShardState expected, TimeSpan? timeout = null)
        {
            timeout ??= 1.Minutes();
            var listener = new ShardStatusWatcher(this, expected, timeout.Value);
            return listener.Task;
        }

        public Task<ShardState> WaitForShardState(string shardName, long sequence, TimeSpan? timeout = null)
        {
            return WaitForShardState(new ShardState(shardName, sequence), timeout);
        }

        public Task<ShardState> WaitForShardState(ShardName name, long sequence, TimeSpan? timeout = null)
        {
            return WaitForShardState(new ShardState(name.Identity, sequence), timeout);
        }

        public Task<ShardState> WaitForShardCondition(Func<ShardState, bool> condition, string description,
            TimeSpan? timeout = null)
        {
            timeout ??= 1.Minutes();
            var listener = new ShardStatusWatcher(description, condition, this, timeout.Value);
            return listener.Task;
        }

        public Task<ShardState> WaitForHighWaterMark(long sequence, TimeSpan? timeout = null)
        {
            return HighWaterMark >= sequence
                ? Task.FromResult(new ShardState(ShardState.HighWaterMark, HighWaterMark))
                : WaitForShardState(ShardState.HighWaterMark, sequence, timeout);
        }

        public Task Complete()
        {
            _block.Complete();
            return _block.Completion;
        }

        public void Dispose()
        {
            _block.Complete();
        }

        private void publish(ShardState state)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Received {ShardState}", state);
            }

            foreach (var observer in _listeners)
            {
                try
                {
                    observer.OnNext(state);
                }
                catch (Exception e)
                {
                    // log them, but never let it through
                    _logger.LogError(e, "Failed to notify subscriber {Subscriber} of shard state {ShardState}", observer, state);
                }
            }
        }

        public void Finish()
        {
            foreach (var observer in _listeners)
                try
                {
                    observer.OnCompleted();
                }
                catch (Exception e)
                {
                    // TODO -- log them, but never let it through
                    Console.WriteLine(e);
                }
        }

        private class Unsubscriber: IDisposable
        {
            private readonly IObserver<ShardState> _observer;
            private readonly ShardStateTracker _tracker;

            public Unsubscriber(ShardStateTracker tracker, IObserver<ShardState> observer)
            {
                _tracker = tracker;
                _observer = observer;
            }

            public void Dispose()
            {
                if (_observer != null) _tracker._listeners = _tracker._listeners.Remove(_observer);
            }
        }
    }
}