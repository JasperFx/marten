namespace Marten.Events.Daemon;

public class NulloDaemonRuntime: IDaemonRuntime
{
    public void Enqueue(DeadLetterEvent @event)
    {
        // Nothing, but at least don't blow up
    }
}
