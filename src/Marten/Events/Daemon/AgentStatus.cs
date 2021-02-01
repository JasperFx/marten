namespace Marten.Events.Daemon
{
    public enum AgentStatus
    {
        /// <summary>
        /// The projection shard is successfully processing new
        /// events
        /// </summary>
        Running,

        /// <summary>
        /// The projection shard has been completely stopped
        /// </summary>
        Stopped,

        /// <summary>
        /// The projection shard has been temporarily paused due
        /// to failures and will be re-started after a set amount
        /// of time
        /// </summary>
        Paused
    }
}
