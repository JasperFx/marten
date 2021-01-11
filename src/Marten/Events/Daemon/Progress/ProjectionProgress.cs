namespace Marten.Events.Daemon.Progress
{
    public class ProjectionProgress
    {
        public long LastSequenceId { get; set; }
        public string ProjectionOrShardName { get; set; }
    }
}
