using System;
using System.Collections.Generic;
using LamarCodeGeneration;
using Marten.Events.Daemon;
using Marten.Linq.SqlGeneration;
using Marten.Storage;
using Weasel.Postgresql.SqlGeneration;

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

        public override Type ProjectionType => _projection.GetType();

        internal override IProjection Build(DocumentStore store)
        {
            return _projection;
        }

        internal override IReadOnlyList<AsyncProjectionShard> AsyncProjectionShards(DocumentStore store)
        {
            return new List<AsyncProjectionShard>
            {
                new AsyncProjectionShard(this, new ISqlFragment[0])
            };
        }
    }
}
