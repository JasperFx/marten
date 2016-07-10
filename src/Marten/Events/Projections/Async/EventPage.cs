using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Events.Projections.Async
{
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

        public EventPage(long from, long to, IList<IEvent> events)
        {
            From = @from;
            To = to;
            Streams = ToStreams(events);
        }

        public override string ToString()
        {
            return $"Event Page From: {From}, To: {To}, Count: {Count}";
        }
    }
}