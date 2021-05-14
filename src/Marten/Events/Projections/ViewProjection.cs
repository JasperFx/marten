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
        private readonly IList<IFanOutRule> _fanOutRules = new List<IFanOutRule>();
        private readonly IList<IGrouper<TId>> _groupers = new List<IGrouper<TId>>();
        private readonly IList<IAggregateGrouper<TId>> _lookupGroupers = new List<IAggregateGrouper<TId>>();


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

            return @group;
        }

        async ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> IEventSlicer<TDoc, TId>.SliceAsyncEvents(
            IQuerySession querySession,
            List<IEvent> events, ITenancy tenancy)
        {
            foreach (var fanOutRule in _fanOutRules) fanOutRule.Apply(events);

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
            return base.determineEventTypes().Concat(_fanOutRules.Select(x => x.OriginatingType))
                .Distinct().ToArray();
        }

        public void Identity<TEvent>(Func<TEvent, TId> identityFunc)
        {
            _groupers.Add(new SingleStreamGrouper<TId, TEvent>(identityFunc));
        }

        public void Identities<TEvent>(Func<TEvent, IReadOnlyList<TId>> identitiesFunc)
        {
            _groupers.Add(new MultiStreamGrouper<TId, TEvent>(identitiesFunc));
        }

        public void CustomGrouping(IAggregateGrouper<TId> grouper)
        {
            _lookupGroupers.Add(grouper);
        }


        protected override void specialAssertValid()
        {
            if (!_groupers.Any() && !_lookupGroupers.Any())
            {
                throw new InvalidProjectionException(
                    $"ViewProjection {GetType().FullNameInCode()} has no Identity() rules defined or registered lookup grouping rules and does not know how to identify event membership in the aggregated document {typeof(TDoc).FullNameInCode()}");
            }
        }

        public void FanOut<TEvent, TChild>(Func<TEvent, IEnumerable<TChild>> fanOutFunc)
        {
            var fanout = new FanOutOperator<TEvent, TChild>(fanOutFunc);
            _fanOutRules.Add(fanout);
        }

        private bool _groupByTenant = false;

        protected override object buildEventSlicer(StoreOptions options)
        {
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
