using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Events.Aggregation;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Storage;

#nullable enable
namespace Marten.Events.Projections
{
    /// <summary>
    /// Project a single document view across events that may span across
    /// event streams in a user-defined grouping
    /// </summary>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TId"></typeparam>
    public abstract class ViewProjection<TDoc, TId>: AggregateProjection<TDoc>, IEventSlicer<TDoc, TId>
    {
        private IViewProjectionEventSlicer<TDoc, TId> _eventSlicer = new ViewProjectionEventSlicer<TDoc, TId>();

        protected ViewProjection()
        {
            Lifecycle = ProjectionLifecycle.Async;
        }

        protected override Type[] determineEventTypes()
        {
            return base.determineEventTypes().Concat(_eventSlicer.Fanouts.Select(x => x.OriginatingType))
                .Distinct().ToArray();
        }

        public void Identity<TEvent>(Func<TEvent, TId> identityFunc)
        {
            var grouper = new Grouper<TId, TEvent>(identityFunc);
            _eventSlicer.Groupers.Add(grouper);
        }

        public void Identities<TEvent>(Func<TEvent, TId[]> identitiesFunc)
        {
            var grouper = new MultiStreamGrouper<TId, TEvent>(identitiesFunc);
            _eventSlicer.Groupers.Add(grouper);
        }

        public void EventSlicer(IViewProjectionEventSlicer<TDoc, TId> eventSlicer)
        {
            eventSlicer.Groupers.AddRange(_eventSlicer.Groupers);
            eventSlicer.Fanouts.AddRange(_eventSlicer.Fanouts);

            _eventSlicer = eventSlicer;
        }

        protected override void specialAssertValid()
        {
            if (!_eventSlicer.Groupers.Any())
            {
                throw new InvalidProjectionException(
                    $"ViewProjection {GetType().FullNameInCode()} has no Identity() rules defined and does not know how to identify event membership in the aggregated document {typeof(TDoc).FullNameInCode()}");
            }
        }

        public void FanOut<TEvent, TChild>(Func<TEvent, IEnumerable<TChild>> fanOutFunc)
        {
            var fanout = new FanOutOperator<TEvent, TChild>(fanOutFunc);
            _eventSlicer.Fanouts.Add(fanout);
        }

        ValueTask<IReadOnlyList<EventSlice<TDoc, TId>>> IEventSlicer<TDoc, TId>.Slice(IQuerySession querySession,
            IEnumerable<StreamAction> streams, ITenancy tenancy)
        {
            return _eventSlicer.Slice(querySession, streams, tenancy);
        }

        ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> IEventSlicer<TDoc, TId>.Slice(IQuerySession querySession,
            IReadOnlyList<IEvent> events, ITenancy tenancy)
        {
            return _eventSlicer.Slice(querySession, events, tenancy);
        }

        protected override object buildEventSlicer()
        {
            return this;
        }

        protected override IEnumerable<string> validateDocumentIdentity(StoreOptions options, DocumentMapping mapping)
        {
            yield break;
        }
    }
}
