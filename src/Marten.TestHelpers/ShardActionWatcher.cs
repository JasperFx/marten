using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events.Daemon;

namespace Marten.Testing;

internal class ShardActionWatcher: IObserver<ShardState>
{
    private readonly IDisposable _unsubscribe;
    private readonly string _shardName;
    private readonly ShardAction _expected;
    private readonly TaskCompletionSource<ShardState> _completion;
    private readonly CancellationTokenSource _timeout;

    public ShardActionWatcher(ShardStateTracker tracker, string shardName, ShardAction expected, TimeSpan timeout)
    {
        _shardName = shardName;
        _expected = expected;
        _completion = new TaskCompletionSource<ShardState>();


        _timeout = new CancellationTokenSource(timeout);
        _timeout.Token.Register(() =>
        {
            _completion.TrySetException(new TimeoutException(
                $"Shard {_shardName} did receive the action {_expected} in the time allowed"));
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
        if (value.ShardName.EqualsIgnoreCase(_shardName) && value.Action == _expected)
        {
            _completion.SetResult(value);
            _unsubscribe.Dispose();
        }
    }
}
