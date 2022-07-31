using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using Marten.Events.CodeGeneration;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Events.Aggregation
{
    public abstract partial class GeneratedAggregateProjectionBase<T>: GeneratedProjection, IAggregateProjection, ILiveAggregatorSource<T>
    {
        private readonly Lazy<Type[]> _allEventTypes;
        internal readonly ApplyMethodCollection _applyMethods;

        internal readonly CreateMethodCollection _createMethods;
        private readonly string _inlineAggregationHandlerType;
        private readonly string _liveAggregationTypeName;
        internal readonly ShouldDeleteMethodCollection _shouldDeleteMethods;
        internal IDocumentMapping _aggregateMapping;
        private GeneratedType _inlineGeneratedType;
        private bool _isAsync;
        private GeneratedType _liveGeneratedType;
        private IAggregationRuntime _runtime;
        private Type _inlineType;
        private Type _liveType;
        private readonly AggregateVersioning<T> _versioning;

        protected GeneratedAggregateProjectionBase(AggregationScope scope) : base(typeof(T).NameInCode())
        {
            _createMethods = new CreateMethodCollection(GetType(), typeof(T));
            _applyMethods = new ApplyMethodCollection(GetType(), typeof(T));
            _shouldDeleteMethods = new ShouldDeleteMethodCollection(GetType(), typeof(T));

            Options.DeleteViewTypeOnTeardown<T>();

            _allEventTypes = new Lazy<Type[]>(() =>
            {
                return _createMethods.Methods.Concat(_applyMethods.Methods).Concat(_shouldDeleteMethods.Methods)
                    .Select(x => x.EventType).Concat(DeleteEvents).Concat(TransformedEvents).Distinct().ToArray();
            });


            _inlineAggregationHandlerType = GetType().ToSuffixedTypeName("InlineHandler");
            _liveAggregationTypeName = GetType().ToSuffixedTypeName("LiveAggregation");
            _versioning = new AggregateVersioning<T>(scope);

            RegisterPublishedType(typeof(T));
        }

        /// <summary>
        /// Designate or override the aggregate version member for this aggregate type
        /// </summary>
        /// <param name="expression"></param>
        public void VersionIdentity(Expression<Func<T, int>> expression)
        {
            _versioning.Override(expression);
        }

        /// <summary>
        /// Designate or override the aggregate version member for this aggregate type
        /// </summary>
        /// <param name="expression"></param>
        public void VersionIdentity(Expression<Func<T, long>> expression)
        {
            _versioning.Override(expression);
        }

        internal IList<Type> DeleteEvents { get; } = new List<Type>();
        internal IList<Type> TransformedEvents { get; } = new List<Type>();

        Type IAggregateProjection.AggregateType => typeof(T);

        protected abstract object buildEventSlicer(StoreOptions documentMapping);
        protected abstract Type baseTypeForAggregationRuntime();

        /// <summary>
        /// When used as an asynchronous projection, this opts into
        /// only taking in events from streams explicitly marked as being
        /// the aggregate type for this projection. Only use this if you are explicitly
        /// marking streams with the aggregate type on StartStream()
        /// </summary>
        [MartenIgnore]
        public void FilterIncomingEventsOnStreamType()
        {
            StreamType = typeof(T);
        }

        public bool AppliesTo(IEnumerable<Type> eventTypes)
        {
            return eventTypes
                .Intersect(AllEventTypes).Any() || eventTypes.Any(type => AllEventTypes.Any(type.CanBeCastTo));
        }

        public Type[] AllEventTypes => _allEventTypes.Value;

        public bool MatchesAnyDeleteType(IEventSlice slice)
        {
            return slice.Events().Select(x => x.EventType).Intersect(DeleteEvents).Any();
        }

        public bool MatchesAnyDeleteType(StreamAction action)
        {
            return action.Events.Select(x => x.EventType).Intersect(DeleteEvents).Any();
        }

        protected sealed override ValueTask<EventRangeGroup> groupEvents(DocumentStore store, IMartenDatabase daemonDatabase, EventRange range,
            CancellationToken cancellationToken)
        {
            _runtime ??= BuildRuntime(store);

            return _runtime.GroupEvents(store, daemonDatabase, range, cancellationToken);
        }

        protected virtual Type[] determineEventTypes()
        {
            var eventTypes = MethodCollection.AllEventTypes(_applyMethods, _createMethods, _shouldDeleteMethods)
                .Concat(DeleteEvents).Concat(TransformedEvents).Distinct().ToArray();
            return eventTypes;
        }

    }
}
