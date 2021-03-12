using System;
using System.Collections.Generic;
using Marten.Events.Daemon;
using Marten.Storage;

namespace Marten.Events.Projections
{
    public enum ProjectionLifecycle
    {
        Inline,
        Async,
        Live
    }

    public interface IProjectionSource
    {
        string ProjectionName { get; }
        ProjectionLifecycle Lifecycle { get; }
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
        internal abstract IReadOnlyList<IAsyncProjectionShard> AsyncProjectionShards(DocumentStore store);

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
    }
}
