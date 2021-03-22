using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon
{
    public abstract class EventRangeGroup: IDisposable
    {
        private readonly CancellationToken _parent;
        private CancellationTokenSource _cancellationTokenSource;

        protected EventRangeGroup(EventRange range, CancellationToken parent)
        {
            _parent = parent;
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

            Cancellation =
                CancellationTokenSource.CreateLinkedTokenSource(_parent, _cancellationTokenSource.Token).Token;
            reset();
        }

        public void Abort()
        {
            WasAborted = true;
            _cancellationTokenSource.Cancel();
        }

        public bool WasAborted { get; private set; }

        public CancellationToken Cancellation { get; private set; }
        public int Attempts { get; private set; } = -1;

        protected abstract void reset();

        public abstract void Dispose();

        public abstract Task ConfigureUpdateBatch(IShardAgent shardAgent, ProjectionUpdateBatch batch);
        public abstract void SkipEventSequence(long eventSequence);
    }
}
