namespace Marten.Events.Daemon
{
    internal class ProjectionAgent
    {
        private readonly DocumentStore _store;
        private readonly IAsyncProjection _projection;

        public ProjectionAgent(DocumentStore store, IAsyncProjection projection)
        {
            _store = store;
            _projection = projection;
        }

        public string ProjectionOrShardName => _projection.ProjectionOrShardName;

        public void MarkHighWater(long sequence)
        {
            // TODO -- decide whether to keep

        }
    }

    internal class EventFetcher
    {

    }
}
