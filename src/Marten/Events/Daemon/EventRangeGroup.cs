using System;
using System.Threading;

namespace Marten.Events.Daemon
{
    internal abstract class EventRangeGroup: IDisposable
    {
        private CancellationTokenSource _cancellationTokenSource;

        protected EventRangeGroup(EventRange range)
        {
            Range = range;

        }

        public EventRange Range { get; }

        /// <summary>
        /// Teardown any existing state. Used to clean off existing work
        /// before doing retries
        /// </summary>
        public void Reset()
        {
            Attempts++;
            WasAborted = false;
            _cancellationTokenSource = new CancellationTokenSource();
            reset();
        }

        public void Abort()
        {
            WasAborted = true;
            _cancellationTokenSource.Cancel();
        }

        public bool WasAborted { get; private set; }

        public CancellationToken GroupCancellation => _cancellationTokenSource.Token;
        public int Attempts { get; private set; } = -1;

        protected abstract void reset();

        public abstract void Dispose();
    }
}
