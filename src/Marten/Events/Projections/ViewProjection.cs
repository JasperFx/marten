using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using Marten.Events.Aggregation;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Storage;

#nullable enable
namespace Marten.Events.Projections
{
    /// <summary>
    ///     Project a single document view across events that may span across
    ///     event streams in a user-defined grouping
    /// </summary>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TId"></typeparam>
    public abstract class ViewProjection<TDoc, TId>: AggregateProjection<TDoc>, IEventSlicer<TDoc, TId>
    {
        private readonly List<IFanOutRule> _beforeGroupingFanoutRules = new List<IFanOutRule>();
        private readonly List<IFanOutRule> _afterGroupingFanoutRules = new List<IFanOutRule>();
        private readonly IList<IGrouper<TId>> _groupers = new List<IGrouper<TId>>();
        private readonly IList<IAggregateGrouper<TId>> _lookupGroupers = new List<IAggregateGrouper<TId>>();
        private bool _groupByTenant = false;
        private IEventSlicer<TDoc, TId>? _customSlicer = null;

        protected ViewProjection()
        {
            Lifecycle = ProjectionLifecycle.Async;
        }

        async ValueTask<IReadOnlyList<EventSlice<TDoc, TId>>> IEventSlicer<TDoc, TId>.SliceInlineActions(
            IQuerySession querySession,
            IEnumerable<StreamAction> streams, ITenancy tenancy)
        {
            var events = streams.SelectMany(x => x.Events).ToList();


            var groups = await this.As<IEventSlicer<TDoc, TId>>().SliceAsyncEvents(querySession, events, tenancy);
            return groups.SelectMany(x => x.Slices).ToList();
        }

        private async Task<TenantSliceGroup<TDoc, TId>> groupSingleTenant(ITenant tenant, IQuerySession querySession, IList<IEvent> events)
        {
            var @group = new TenantSliceGroup<TDoc, TId>(tenant);

            foreach (var grouper in _groupers)
            {
                grouper.Apply(events, @group);
            }

            foreach (var lookupGrouper in _lookupGroupers)
            {
                await lookupGrouper.Group(querySession, events, @group);
            }

            group.ApplyFanOutRules(_afterGroupingFanoutRules);

            return @group;
        }

        async ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> IEventSlicer<TDoc, TId>.SliceAsyncEvents(
            IQuerySession querySession,
            List<IEvent> events, ITenancy tenancy)
        {
            foreach (var fanOutRule in _beforeGroupingFanoutRules)
            {
                fanOutRule.Apply(events);
            }

            if (_groupByTenant)
            {
                var byTenant = events.GroupBy(x => x.TenantId);
                var groupTasks = byTenant.Select(tGroup =>
                {
                    var tenant = tenancy[tGroup.Key];
                    return groupSingleTenant(tenant, querySession.ForTenant(tGroup.Key), tGroup.ToList());
                });

                var list = new List<TenantSliceGroup<TDoc, TId>>();
                foreach (var groupTask in groupTasks)
                {
                    list.Add(await groupTask);
                }

                return list;
            }

            var group = await groupSingleTenant(tenancy.Default, querySession, events);

            return new List<TenantSliceGroup<TDoc, TId>> {group};
        }

        protected override Type[] determineEventTypes()
        {
            return base.determineEventTypes().Concat(_beforeGroupingFanoutRules.Concat(_afterGroupingFanoutRules).Select(x => x.OriginatingType))
                .Distinct().ToArray();
        }

        public void Identity<TEvent>(Func<TEvent, TId> identityFunc)
        {
            if (_customSlicer != null)
                throw new InvalidOperationException(
                    "There is already a custom event slicer registered for this projection");
            _groupers.Add(new SingleStreamGrouper<TId, TEvent>(identityFunc));
        }

        public void Identities<TEvent>(Func<TEvent, IReadOnlyList<TId>> identitiesFunc)
        {
            if (_customSlicer != null)
                throw new InvalidOperationException(
                    "There is already a custom event slicer registered for this projection");
            _groupers.Add(new MultiStreamGrouper<TId, TEvent>(identitiesFunc));
        }

        /// <summary>
        /// Apply a custom event grouping strategy for events. This is additive to Identity() or Identities()
        /// </summary>
        /// <param name="grouper"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void CustomGrouping(IAggregateGrouper<TId> grouper)
        {
            if (_customSlicer != null)
                throw new InvalidOperationException(
                    "There is already a custom event slicer registered for this projection");
            _lookupGroupers.Add(grouper);
        }

        /// <summary>
        /// If your grouping of events to aggregates doesn't fall into any simple pattern supported
        /// directly by ViewProjection, supply your own "let me do whatever I want" event slicer
        /// </summary>
        /// <param name="slicer"></param>
        public void CustomGrouping(IEventSlicer<TDoc, TId> slicer)
        {
            _customSlicer = slicer;
        }


        protected override void specialAssertValid()
        {
            if (_customSlicer == null && !_groupers.Any() && !_lookupGroupers.Any())
            {
                throw new InvalidProjectionException(
                    $"ViewProjection {GetType().FullNameInCode()} has no Identity() rules defined or registered lookup grouping rules and does not know how to identify event membership in the aggregated document {typeof(TDoc).FullNameInCode()}");
            }
        }

        /// <summary>
        /// Apply "fan out" operations to the given TEvent type that inserts an enumerable of TChild events right behind the parent
        /// event in the event stream
        /// </summary>
        /// <param name="fanOutFunc"></param>
        /// <param name="mode">Should the fan out operation happen after grouping, or before? Default is after</param>
        /// <typeparam name="TEvent"></typeparam>
        /// <typeparam name="TChild"></typeparam>
        public void FanOut<TEvent, TChild>(Func<TEvent, IEnumerable<TChild>> fanOutFunc, FanoutMode mode = FanoutMode.AfterGrouping)
        {
            var fanout = new FanOutOperator<TEvent, TChild>(fanOutFunc)
            {
                Mode = mode
            };

            switch (mode)
            {
                case FanoutMode.AfterGrouping:
                    _afterGroupingFanoutRules.Add(fanout);
                    break;

                case FanoutMode.BeforeGrouping:
                    _beforeGroupingFanoutRules.Add(fanout);
                    break;
            }

        }



        protected override object buildEventSlicer(StoreOptions options)
        {
            if (_customSlicer != null) return _customSlicer;

            var mapping = options.Storage.MappingFor(typeof(TDoc));
            var aggregateStyle = mapping.TenancyStyle;
            var eventStyle = options.Events.TenancyStyle;

            if (aggregateStyle == TenancyStyle.Conjoined)
            {
                if (eventStyle == TenancyStyle.Conjoined)
                {
                    _groupByTenant = true;
                }
                else
                {
                    throw new InvalidProjectionException(
                        $"Aggregate {typeof(TDoc).FullNameInCode()} is multi-tenanted, but the events are not");
                }
            }

            return this;
        }

        protected override IEnumerable<string> validateDocumentIdentity(StoreOptions options, DocumentMapping mapping)
        {
            yield break;
        }
    }
}
