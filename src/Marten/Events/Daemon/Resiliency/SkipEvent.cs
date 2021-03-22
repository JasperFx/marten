namespace Marten.Events.Daemon.Resiliency
{
    internal class SkipEvent: IContinuation
    {
        public SkipEvent()
        {
        }

        public IEvent Event { get; set; }
    }
}
