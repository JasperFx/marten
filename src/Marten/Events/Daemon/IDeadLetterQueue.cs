namespace Marten.Events.Daemon;

public interface IDaemonRuntime
{
    void Enqueue(DeadLetterEvent @event);
}
