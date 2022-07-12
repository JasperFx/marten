using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Storage;

namespace Marten.Events.Projections
{
    public interface IProjectionSource : IReadOnlyProjectionData
    {
        AsyncOptions Options { get; }

        /// <summary>
        /// This is *only* a hint to Marten about what projected document types
        /// are published by this projection to aid the "generate ahead" model
        /// </summary>
        /// <returns></returns>
        IEnumerable<Type> PublishedTypes();

        IReadOnlyList<AsyncProjectionShard> AsyncProjectionShards(DocumentStore store);

        ValueTask<EventRangeGroup> GroupEvents(DocumentStore store, IMartenDatabase daemonDatabase,
            EventRange range,
            CancellationToken cancellationToken);

        IProjection Build(DocumentStore store);
    }

}
