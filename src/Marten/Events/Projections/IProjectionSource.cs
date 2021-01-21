using System.Collections.Generic;
using Marten.Events.Daemon;
using Marten.Storage;

namespace Marten.Events.Projections
{
    internal interface IProjectionSource
    {
        string ProjectionName { get; }

        IProjection Build(DocumentStore store);

        IReadOnlyList<IAsyncProjectionShard> AsyncProjectionShards(IDocumentStore store, ITenancy tenancy);
    }
}
