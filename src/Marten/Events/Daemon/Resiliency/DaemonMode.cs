namespace Marten.Events.Daemon.Resiliency
{
    public enum DaemonMode
    {
        Disabled,
        Solo,
        HotCold,
        Distributed
    }
}