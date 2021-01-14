using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Baseline;

namespace Marten.Events.Daemon
{
    public class ShardState
    {
        public ShardState(string shardName, long sequence)
        {
            ShardName = shardName;
            Sequence = sequence;
            Timestamp = DateTimeOffset.UtcNow;
        }

        public DateTimeOffset Timestamp { get; }

        public string ShardName { get; }
        public long Sequence { get; }
    }

    public class ShardStateTracker: IObservable<ShardState>
    {
        // TODO -- use an immutable list here
        private readonly List<IObserver<ShardState>> _listeners = new List<IObserver<ShardState>>();

        public IDisposable Subscribe(IObserver<ShardState> observer)
        {
            _listeners.Fill(observer);
            return new Unsubscriber(_listeners, observer);
        }

        public void Publish(ShardState state)
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
            {
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
        }

        private class Unsubscriber : IDisposable
        {
            private readonly List<IObserver<ShardState>> _observers;
            private readonly IObserver<ShardState> _observer;

            public Unsubscriber(List<IObserver<ShardState>> observers, IObserver<ShardState> observer)
            {
                _observers = observers;
                _observer = observer;
            }

            public void Dispose()
            {
                if (_observer != null) _observers.Remove(_observer);
            }
        }
    }
}
