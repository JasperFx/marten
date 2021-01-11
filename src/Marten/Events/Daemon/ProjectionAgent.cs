namespace Marten.Events.Daemon
{
    public class ProjectionAgent
    {
        private readonly DocumentStore _store;
        private readonly IAsyncProjection _projection;

        public ProjectionAgent(DocumentStore store, IAsyncProjection projection)
        {
            _store = store;
            _projection = projection;
        }

        public string ProjectionOrShardName => _projection.ProjectionOrShardName;
    }
}
