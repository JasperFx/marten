namespace Marten.Events.Daemon.Resiliency
{
    public enum DaemonMode
    {
        /// <summary>
        /// The projection daemon is disabled in this Marten application and
        /// will not be started as part of the application
        /// </summary>
        Disabled,

        /// <summary>
        /// Marten will start up the complete projection daemon with the assumption
        /// that this node is the only execution node. This is appropriate for single
        /// node deployments and local development usage
        /// </summary>
        Solo,

        /// <summary>
        /// Marten will ensure that the full async projection daemon will only execute on
        /// one node at a time, with fail over to other nodes.
        /// </summary>
        HotCold,

    }
}
