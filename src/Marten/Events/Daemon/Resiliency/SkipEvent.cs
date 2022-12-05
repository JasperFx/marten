namespace Marten.Events.Daemon.Resiliency;

internal class SkipEvent: IContinuation
{
    public IEvent Event { get; set; }
}
