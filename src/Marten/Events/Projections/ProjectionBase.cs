using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Events.Daemon;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Projections
{
    public abstract class ProjectionBase
    {
        private readonly IList<ISqlFragment> _filters = new List<ISqlFragment>();

        /// <summary>
        /// Descriptive name for this projection in the async daemon. The default is the type name of the projection
        /// </summary>
        public string ProjectionName { get; set; }

        /// <summary>
        /// The projection lifecycle that governs when this projection is executed
        /// </summary>
        public ProjectionLifecycle Lifecycle { get; set; } = ProjectionLifecycle.Async;

        internal ISqlFragment[] BuildFilters(DocumentStore store)
        {
            return buildFilters(store).ToArray();
        }

        private IEnumerable<ISqlFragment> buildFilters(DocumentStore store)
        {
            if (IncludedEventTypes.Any() && !IncludedEventTypes.Any(x => x.IsAbstract || x.IsInterface))
            {
                yield return new EventTypeFilter(store.Options.EventGraph, IncludedEventTypes.ToArray());
            }

            if (StreamType != null)
            {
                yield return new AggregateTypeFilter(StreamType, store.Options.EventGraph);
            }

            foreach (var filter in _filters)
            {
                yield return filter;
            }

        }

        /// <summary>
        /// Optimize this projection within the Async Daemon by
        /// limiting the event types processed through this projection
        /// to include type "T". This is inclusive.
        ///
        /// If this list is empty, the async daemon will fetch every possible
        /// type of event at runtime
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public IList<Type> IncludedEventTypes { get; } = new List<Type>();

        /// <summary>
        /// Short hand syntax to tell Marten that this projection takes in the event type T
        /// This is not mandatory, but can be used to optimize the asynchronous projections
        /// to create an "allow list" in the IncludedEventTypes collection
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public void IncludeType<T>()
        {
            IncludedEventTypes.Add(typeof(T));
        }

        /// <summary>
        /// Limit the events processed by this projection to only streams
        /// marked with this stream type
        /// </summary>
        internal Type StreamType { get; set; }

        /// <summary>
        /// Limit the events processed by this projection to only streams
        /// marked with the given streamType.
        ///
        /// ONLY APPLIED TO ASYNCHRONOUS PROJECTIONS
        /// </summary>
        /// <param name="streamType"></param>
        public void FilterIncomingEventsOnStreamType(Type streamType)
        {
            StreamType = streamType;
        }

        internal virtual void AssembleAndAssertValidity()
        {
            // Nothing
        }
    }
}
