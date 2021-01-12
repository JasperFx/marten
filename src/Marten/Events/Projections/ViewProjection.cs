using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using Marten.Events.Aggregation;
using Marten.Exceptions;
using Marten.Storage;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Project a single document view across events that may span across
    /// event streams in a user-defined grouping
    /// </summary>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TId"></typeparam>
    public abstract class ViewProjection<TDoc, TId> : AggregateProjection<TDoc>, IEventSlicer<TDoc, TId>
    {
        private readonly IList<IGrouper<TId>> _groupers = new List<IGrouper<TId>>();

        public void Identity<TEvent>(Func<TEvent, TId> expression)
        {
            var grouper = new Grouper<TEvent>(expression);
            _groupers.Add(grouper);
        }

        private interface IGrouper<TId>
        {
            TId FindId(IEvent @event);
        }

        private class Grouper<TEvent> : IGrouper<TId>
        {
            private readonly Func<TEvent, TId> _func;

            public Grouper(Func<TEvent, TId> expression)
            {
                // TODO -- it's possible we'll use the expression later to write metadata into the events table
                // to support the async daemon, but I'm doing it the easy way for now
                _func = expression;
            }

            public TId FindId(IEvent @event)
            {
                if (@event.Data is TEvent e) return _func(e);

                return default;
            }
        }

        protected override void specialAssertValid()
        {
            if (!_groupers.Any())
            {
                throw new InvalidProjectionException(
                    $"ViewProjection {GetType().FullNameInCode()} has no Identity() rules defined and does not know how to identify event membership in the aggregated document {typeof(TDoc).FullNameInCode()}");
            }
        }

        public void FanOut<TEvent, TChild>(Expression<Func<TEvent, IEnumerable<TChild>>> expression)
        {

            throw new NotImplementedException();
        }

        public IReadOnlyList<EventSlice<TDoc, TId>> Slice(IEnumerable<StreamAction> streams, ITenancy tenancy)
        {
            return slice(streams, tenancy).ToList();
        }

        private bool tryFindId(IEvent @event, out TId id)
        {
            foreach (var grouper in _groupers)
            {
                id = grouper.FindId(@event);
                if (!id.Equals(default(TId))) return true;
            }

            id = default;
            return false;
        }

        public IEnumerable<EventSlice<TDoc, TId>> slice(IEnumerable<StreamAction> streams, ITenancy tenancy)
        {
            var groups = streams.SelectMany(x => x.Events).GroupBy(x => x.TenantId);
            foreach (var @group in groups)
            {
                var tenant = tenancy[@group.Key];
                var slices =
                    new LightweightCache<TId, EventSlice<TDoc, TId>>(id => new EventSlice<TDoc, TId>(id, tenant));

                foreach (var @event in group)
                {
                    if (tryFindId(@event, out var id))
                    {
                        slices[id].AddEvent(@event);
                    }
                }

                foreach (var eventSlice in slices)
                {
                    yield return eventSlice;
                }
            }
        }

        public IReadOnlyList<EventSlice<TDoc, TId>> Slice(IReadOnlyList<IEvent> events, ITenancy tenancy)
        {
            throw new NotImplementedException();
        }

        protected override object buildEventSlicer()
        {
            return this;
        }
    }
}
