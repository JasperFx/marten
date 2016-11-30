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
        public static EventStream[] ToStreams(IEnumerable<IEvent> events)
        {
            return events.GroupBy(x => x.StreamId)
                        .Select(
                            group =>
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
        public EventStream[] Streams { get; set; }
        public int Count { get; set; }
        public EventPage Next { get; set; }

        public IList<long> Sequences { get; set; } = new List<long>();

        public long NextKnownSequence { get; set; }

        public long LastKnownSequence { get; set; }

        public long LastEncountered()
        {
            if (Streams.Any())
            {
                return Sequences.Any() ? Sequences.Last() : Streams.SelectMany(x => x.Events).Max(x => x.Sequence);
            }

            return NextKnownSequence > 0 ? NextKnownSequence - 1 : LastKnownSequence;
        }

        public EventPage(long from, long to, IList<IEvent> events)
        {
            From = @from;
            To = to;
            Streams = ToStreams(events);
        }

        public EventPage(long @from, IList<long> sequences, IList<IEvent> events)
        {
            Sequences = sequences;
            From = @from;
            To = Sequences.LastOrDefault();
            Streams = ToStreams(events);
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

        public override string ToString()
        {
            return $"Event Page From: {From}, To: {To}, Count: {Count}";
        }
    }
}