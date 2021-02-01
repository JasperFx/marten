using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon
{
    internal class ShardStatusWatcher: IObserver<ShardState>
    {
        private readonly IDisposable _unsubscribe;
        private readonly Func<ShardState, bool> _condition;
        private readonly TaskCompletionSource<ShardState> _completion;

        public ShardStatusWatcher(ShardStateTracker tracker, ShardState expected, TimeSpan timeout)
        {
            _condition = x => x.Equals(expected);
            _completion = new TaskCompletionSource<ShardState>();


            var timeout1 = new CancellationTokenSource(timeout);
            timeout1.Token.Register(() =>
            {
                _completion.TrySetException(new TimeoutException(
                    $"Shard {expected.ShardName} did not reach sequence number {expected.Sequence} in the time allowed"));
            });

            _unsubscribe = tracker.Subscribe(this);
        }

        public ShardStatusWatcher(string description, Func<ShardState, bool> condition, ShardStateTracker tracker, TimeSpan timeout)
        {
            _condition = condition;
            _completion = new TaskCompletionSource<ShardState>();


            var timeout1 = new CancellationTokenSource(timeout);
            timeout1.Token.Register(() =>
            {
                _completion.TrySetException(new TimeoutException(
                    $"{description} was not detected in the time allowed"));
            });

            _unsubscribe = tracker.Subscribe(this);
        }

        public Task<ShardState> Task => _completion.Task;

        public void OnCompleted()
        {

        }

        public void OnError(Exception error)
        {
            _completion.SetException(error);
        }

        public void OnNext(ShardState value)
        {
            if (_condition(value))
            {
                _completion.SetResult(value);
                _unsubscribe.Dispose();
            }
        }
    }
}