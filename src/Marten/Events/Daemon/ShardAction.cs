namespace Marten.Events.Daemon
{
    public enum ShardAction
    {
        /// <summary>
        /// The projection shard updated successfully
        /// </summary>
        Updated,

        /// <summary>
        /// The projection shard was successfully started
        /// </summary>
        Started,

        /// <summary>
        /// The projection shard was stopped
        /// </summary>
        Stopped,

        /// <summary>
        /// The projection shard was paused and will be restarted
        /// after a set amount of time based on error handling policies
        /// </summary>
        Paused
    }
}
