namespace Marten.Events.Projections.Async
{
    public class EventPage
    {
        public long From { get; set; }
        public long To { get; set; }
        public EventStream[] Streams { get; set; }
        public int Count { get; set; }
        public EventPage Next { get; set; }

        public EventPage(long from, long to, EventStream[] streams)
        {
            From = @from;
            To = to;
            Streams = streams;
        }
    }
}