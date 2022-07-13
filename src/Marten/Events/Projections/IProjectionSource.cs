using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Storage;
using Weasel.Core;

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

    /// <summary>
    /// Expose functionality on a projection source or IProjection to
    /// optionally delete existing projected data as the first step in
    /// rebuilding projections
    /// </summary>
    public interface IProjectionSourceTeardown
    {
        Task TeardownExistingData(IDocumentStore store, IMartenDatabase database,
            CancellationToken cancellation);
    }

    /// <summary>
    /// Optional interface to expose additional schema objects to be
    /// built as part of the event store
    /// </summary>
    public interface IProjectionSchemaSource
    {
        IReadOnlyList<ISchemaObject> SchemaObjects();
    }

}
