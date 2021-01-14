using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Marten.Events.Daemon
{
    public class ShardState
    {
        public const string HighWaterMark = "HighWaterMark";

        public ShardState(string shardName, long sequence)
        {
            ShardName = shardName;
            Sequence = sequence;
            Timestamp = DateTimeOffset.UtcNow;
        }

        public DateTimeOffset Timestamp { get; }

        public string ShardName { get; }
        public long Sequence { get; }

        public override string ToString()
        {
            return $"{nameof(ShardName)}: {ShardName}, {nameof(Sequence)}: {Sequence}";
        }

        protected bool Equals(ShardState other)
        {
            return ShardName == other.ShardName && Sequence == other.Sequence;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ShardState) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((ShardName != null ? ShardName.GetHashCode() : 0) * 397) ^ Sequence.GetHashCode();
            }
        }
    }

    public class ShardStateTracker: IObservable<ShardState>, IDisposable
    {
        private ImmutableList<IObserver<ShardState>> _listeners = ImmutableList<IObserver<ShardState>>.Empty;
        private readonly ActionBlock<ShardState> _block;

        public ShardStateTracker()
        {
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

        public void Publish(ShardState state)
        {
            if (state.ShardName == ShardState.HighWaterMark)
            {
                HighWaterMark = state.Sequence;
            }

            _block.Post(state);
        }

        public long HighWaterMark { get; private set; }

        public void MarkHighWater(long sequence)
        {
            Publish(new ShardState(ShardState.HighWaterMark, sequence));
        }

        public Task Complete()
        {
            return _block.Completion;
        }

        public void Dispose()
        {
            _block.Complete();
        }

        private void publish(ShardState state)
        {
            foreach (var observer in _listeners)
            {
                try
                {
                    observer.OnNext(state);
                }
                catch (Exception e)
                {
                    // TODO -- log them, but never let it through
                    Console.WriteLine(e);
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
