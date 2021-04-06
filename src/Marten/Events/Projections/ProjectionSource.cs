using System;
using System.Collections.Generic;
using System.Threading;
using Marten.Events.Daemon;
using Marten.Storage;
#nullable enable
namespace Marten.Events.Projections
{
    public enum ProjectionLifecycle
    {
        /// <summary>
        /// The projection will be updated in the same transaction as
        /// the events being captured
        /// </summary>
        Inline,

        /// <summary>
        /// The projection will only execute within the Async Daemon
        /// </summary>
        Async,

        /// <summary>
        /// The projection is only executed on demand
        /// </summary>
        Live
    }

    /// <summary>
    /// Read-only diagnostic view of a registered projection
    /// </summary>
    public interface IProjectionSource
    {
        /// <summary>
        /// The configured projection name used within the Async Daemon
        /// progress tracking
        /// </summary>
        string ProjectionName { get; }

        /// <summary>
        /// When is this projection executed?
        /// </summary>
        ProjectionLifecycle Lifecycle { get; }

        /// <summary>
        /// The concrete .Net type implementing this projection
        /// </summary>
        Type ProjectionType { get; }
    }

    public abstract class ProjectionSource: IProjectionSource
    {
        public string ProjectionName { get; protected internal set; }

        protected ProjectionSource(string projectionName)
        {
            ProjectionName = projectionName;
        }

        public abstract Type ProjectionType { get;}

        public ProjectionLifecycle Lifecycle { get; set; } = ProjectionLifecycle.Inline;

        internal abstract IProjection Build(DocumentStore store);
        internal abstract IReadOnlyList<AsyncProjectionShard> AsyncProjectionShards(DocumentStore store);

        public AsyncOptions Options { get; } = new AsyncOptions();

        internal virtual void AssertValidity()
        {
            // Nothing
        }

        internal virtual IEnumerable<string> ValidateConfiguration(StoreOptions options)
        {
            // Nothing
            yield break;
        }

        private IProjection? _projection;

        internal virtual EventRangeGroup GroupEvents(
            DocumentStore store,
            EventRange range,
            CancellationToken cancellationToken)
        {
            _projection ??= Build(store);

            return new TenantedEventRange(store, _projection, range, cancellationToken);
        }
    }
}
