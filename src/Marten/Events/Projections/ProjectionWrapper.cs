using System.Collections.Generic;
using LamarCodeGeneration;
using Marten.Events.Daemon;
using Marten.Linq.SqlGeneration;
using Marten.Storage;

namespace Marten.Events.Projections
{
    internal class ProjectionWrapper: ProjectionSource
    {
        private readonly IProjection _projection;

        public ProjectionWrapper(IProjection projection, ProjectionLifecycle lifecycle) : base(projection.GetType().FullNameInCode())
        {
            _projection = projection;
            Lifecycle = lifecycle;
        }

        internal override IProjection Build(DocumentStore store)
        {
            return _projection;
        }

        internal override IReadOnlyList<IAsyncProjectionShard> AsyncProjectionShards(DocumentStore store)
        {
            var shard = new AsyncProjectionShard(new ShardName(ProjectionName), _projection, System.Array.Empty<ISqlFragment>(), (DocumentStore) store, Options);
            return new List<IAsyncProjectionShard> {shard};
        }
    }
}
