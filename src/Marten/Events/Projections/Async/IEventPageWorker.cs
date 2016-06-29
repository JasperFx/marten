namespace Marten.Events.Projections.Async
{
    public interface IEventPageWorker
    {
        void QueuePage(EventPage page);
        void Finished(long lastEncountered);
    }
}