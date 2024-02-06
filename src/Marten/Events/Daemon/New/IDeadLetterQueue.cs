namespace Marten.Events.Daemon.New;

public interface IDaemonRuntime
{
    void Enqueue(DeadLetterEvent @event);
}
