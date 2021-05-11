namespace Marten.Events.Daemon
{
    public enum GroupActionMode
    {
        /// <summary>
        /// If the action is at the parent level, you can skip events
        /// and retry from here
        /// </summary>
        Parent,

        /// <summary>
        /// If the action is at the child level, the daemon error handling
        /// cannot skip events at this level, but needs to be retried
        /// from the parent action level
        /// </summary>
        Child
    }
}
