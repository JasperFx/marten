namespace Marten.Events.Daemon.Progress
{
    // TODO -- replace this with ShardState???
    public class ProjectionProgress
    {
        public long LastSequenceId { get; set; }
        public string ProjectionOrShardName { get; set; }
    }
}
