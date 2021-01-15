using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Events.Daemon
{
    internal class ProjectionController
    {
        private readonly string _shardName;
        private readonly IProjectionUpdater _updater;
        private readonly AsyncOptions _options;

        private readonly Queue<EventRange> _inFlight = new Queue<EventRange>();


        public ProjectionController(string shardName, IProjectionUpdater updater, AsyncOptions options)
        {
            _shardName = shardName;
            _updater = updater;
            _options = options ?? new AsyncOptions();
        }

        public int InFlightCount => _inFlight.Sum(x => x.Size);

        public void MarkHighWater(long sequence)
        {
            HighWaterMark = sequence;

            enqueueNewEventRanges();
        }

        public void Start(long highWaterMark, long lastCommitted)
        {
            if (lastCommitted > highWaterMark)
            {
                throw new InvalidOperationException(
                    $"The last committed number ({lastCommitted}) cannot be higher than the high water mark ({highWaterMark})");
            }

            HighWaterMark = highWaterMark;
            LastCommitted = LastEnqueued = lastCommitted;

            enqueueNewEventRanges();
        }

        private void enqueueNewEventRanges()
        {
            while (HighWaterMark > LastEnqueued && InFlightCount < _options.MaximumHopperSize)
            {
                var floor = LastEnqueued;
                var ceiling = LastEnqueued + _options.BatchSize;
                if (ceiling > HighWaterMark)
                {
                    ceiling = HighWaterMark;
                }

                startRange(floor, ceiling);
            }
        }

        private void startRange(long floor, long ceiling)
        {
            var range = new EventRange(_shardName, floor, ceiling);
            LastEnqueued = range.SequenceCeiling;
            _inFlight.Enqueue(range);
            _updater.StartRange(range);
        }

        public long LastEnqueued { get; private set; }

        public long LastCommitted { get; private set; }

        public long HighWaterMark { get; private set; }

        public void EventRangeUpdated(EventRange range)
        {
            LastCommitted = range.SequenceCeiling;
            if (Equals(range, _inFlight.Peek()))
            {
                _inFlight.Dequeue();
            }

            enqueueNewEventRanges();
        }
    }
}
