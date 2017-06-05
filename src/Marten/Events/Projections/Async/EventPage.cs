using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Events.Projections.Async
{
    public enum EventPageType
    {
        Empty,
        Sequential,
        NonSequential,
        Matching
    }

    public class EventPage
    {
        private EventStream[] _streams;
        private IList<IEvent> _events;

        public static EventStream[] ToStreams(StreamIdentity streamIdentity, IEnumerable<IEvent> events)
        {
            if (streamIdentity == StreamIdentity.AsGuid)
            {
                return events.GroupBy(x => x.StreamId)
                    .Select(group =>
                    {
                        return new EventStream(group.Key, group.OrderBy(x => x.Version).ToArray(), false);
                    })
                    .ToArray();
            }

            return events.GroupBy(x => x.StreamKey)
                .Select(group =>
                {
                    return new EventStream(group.Key, group.OrderBy(x => x.Version).ToArray(), false);
                })
                .ToArray();
        }

        public static bool IsCompletelySequential(IList<long> sequences)
        {
            for (int i = 0; i < sequences.Count - 1; i++)
            {
                if (sequences[i + 1] - sequences[i] != 1) return false;
            }

            return true;
        }

        public long From { get; set; }
        public long To { get; set; }

        public EventStream[] Streams => _streams ?? (_streams = ToStreams(StreamIdentity, Events));

        public int Count => Events.Count;
        public EventPage Next { get; set; }

        public IList<long> Sequences { get; set; } = new List<long>();

        public long NextKnownSequence { get; set; }

        public long LastKnownSequence { get; set; }

        public long LastEncountered()
        {
            var candidate = NextKnownSequence > 0 ? NextKnownSequence - 1 : LastKnownSequence;

            if (candidate > 0) return candidate;

            if (Sequences.Any()) return Sequences.Last();

            return From;
        }

        public EventPage(EventStream[] streams)
        {
            _streams = streams;
            From = 0;
            To = 0;
        }

        public EventPage(long from, long to, IList<IEvent> events)
        {
            _events = events;
            From = @from;
            To = to;
        }

        public StreamIdentity StreamIdentity { get; set; } = StreamIdentity.AsGuid;

        public IList<IEvent> Events
        {
            get
            {
                if (_events == null && _streams != null)
                {
                    _events = _streams.SelectMany(x => x.Events).OrderBy(x => x.Sequence).ToList();
                }

                return _events;
            }
        }

        public EventPage(long @from, IList<long> sequences, IList<IEvent> events)
        {
            _events = events;
            Sequences = sequences;
            From = @from;
            To = Sequences.LastOrDefault();
        }

        public bool IsSequential()
        {
            if (!Sequences.Any()) return false;

            var startsAfterFrom = Sequences[0] - From == 1;
            return startsAfterFrom && IsCompletelySequential(Sequences) ;
        }

        public bool CanContinueProcessing(IList<long> previous)
        {
            if (IsSequential()) return true;

            return (Sequences ?? new List<long>()).SequenceEqual(previous);
        }

        public bool ShouldPause()
        {
            return NextKnownSequence == 0;
        }

        public long Ending()
        {
            return Sequences.Any() ? To : From;
        }

        public override string ToString()
        {
            return $"Event Page From: {From}, To: {To}, Count: {Count}";
        }
    }
}