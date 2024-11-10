using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using Marten.Events.Projections;
using Marten.Storage;

namespace Marten.Events.Aggregation;

public class MartenEventSlicer<TDoc, TId>: IMartenEventSlicer<TDoc, TId>
{
    private readonly List<IFanOutRule> _afterGroupingFanoutRules = new();
    private readonly List<IFanOutRule> _beforeGroupingFanoutRules = new();
    private readonly IList<IGrouper<TId>> _groupers = new List<IGrouper<TId>>();
    private readonly IList<IAggregateGrouper<TId>> _lookupGroupers = new List<IAggregateGrouper<TId>>();
    private bool _groupByTenant;

    public async ValueTask<IReadOnlyList<JasperFx.Events.Grouping.EventSliceGroup<TDoc, TId>>> SliceAsyncEvents(
        IQuerySession querySession,
        List<IEvent> events)
    {
        foreach (var fanOutRule in _beforeGroupingFanoutRules) fanOutRule.Apply(events);

        if (_groupByTenant)
        {
            var byTenant = events.GroupBy(x => x.TenantId);
            var groupTasks = byTenant.Select(async tGroup =>
            {
                var tenant = new Tenant(tGroup.Key, querySession.Database);
                return await groupSingleTenant(tenant, querySession.ForTenant(tGroup.Key), tGroup.ToList())
                    .ConfigureAwait(false);
            });

            var list = new List<JasperFx.Events.Grouping.EventSliceGroup<TDoc, TId>>();
            foreach (var groupTask in groupTasks) list.Add(await groupTask.ConfigureAwait(false));

            return list;
        }

        // This path is for *NOT* conjoined multi-tenanted projections, but we have to respect per-database tenancy
        var group = await groupSingleTenant(new Tenant(StorageConstants.DefaultTenantId, querySession.Database), querySession,
            events).ConfigureAwait(false);

        return new List<JasperFx.Events.Grouping.EventSliceGroup<TDoc, TId>> { group };
    }

    public MartenEventSlicer<TDoc, TId> GroupByTenant()
    {
        _groupByTenant = true;
        return this;
    }

    internal bool HasAnyRules()
    {
        return _groupers.Any() || _lookupGroupers.Any() || _lookupGroupers.Any();
    }

    internal IEnumerable<Type> DetermineEventTypes()
    {
        foreach (var rule in _beforeGroupingFanoutRules) yield return rule.OriginatingType;

        foreach (var rule in _afterGroupingFanoutRules) yield return rule.OriginatingType;
    }

    public MartenEventSlicer<TDoc, TId> Identity<TEvent>(Func<TEvent, TId> identityFunc)
    {
        var eventType = typeof(TEvent);
        // Check if we are actually dealing with an IEvent<EventType>
        if (eventType.IsGenericType && eventType.GetGenericTypeDefinition() == typeof(IEvent<>))
        {
            var actualEventType = eventType.GetGenericArguments().First();
            var eventGrouperType = typeof(SingleStreamEventGrouper<,>).MakeGenericType( typeof(TId), actualEventType);
            _groupers.Add((IGrouper<TId>) Activator.CreateInstance(eventGrouperType, identityFunc));
            return this;
        }

        _groupers.Add(new SingleStreamGrouper<TId, TEvent>(identityFunc));
        return this;
    }

    public MartenEventSlicer<TDoc, TId> Identities<TEvent>(Func<TEvent, IReadOnlyList<TId>> identitiesFunc)
    {
        var eventType = typeof(TEvent);
        // Check if we are actually dealing with an IEvent<EventType>
        if (eventType.IsGenericType && eventType.GetGenericTypeDefinition() == typeof(IEvent<>))
        {
            var actualEventType = eventType.GetGenericArguments().First();
            var eventGrouperType = typeof(MultiStreamGrouperWithMetadata<,>).MakeGenericType( typeof(TId), actualEventType);
            _groupers.Add((IGrouper<TId>) Activator.CreateInstance(eventGrouperType, identitiesFunc));
            return this;
        }

        _groupers.Add(new MultiStreamGrouper<TId, TEvent>(identitiesFunc));
        return this;
    }

    /// <summary>
    ///     Apply a custom event grouping strategy for events. This is additive to Identity() or Identities()
    /// </summary>
    /// <param name="grouper"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public MartenEventSlicer<TDoc, TId> CustomGrouping(IAggregateGrouper<TId> grouper)
    {
        _lookupGroupers.Add(grouper);

        return this;
    }

    /// <summary>
    ///     Apply "fan out" operations to the given TEvent type that inserts an enumerable of TChild events right behind the
    ///     parent
    ///     event in the event stream
    /// </summary>
    /// <param name="fanOutFunc"></param>
    /// <param name="mode">Should the fan out operation happen after grouping, or before? Default is after</param>
    /// <typeparam name="TEvent"></typeparam>
    /// <typeparam name="TChild"></typeparam>
    public MartenEventSlicer<TDoc, TId> FanOut<TEvent, TChild>(Func<TEvent, IEnumerable<TChild>> fanOutFunc,
        FanoutMode mode = FanoutMode.AfterGrouping)
    {
        return FanOut(new FanOutEventDataOperator<TEvent, TChild>(fanOutFunc) { Mode = mode }, mode);
    }

    /// <summary>
    ///     Apply "fan out" operations to the given TEvent type that inserts an enumerable of TChild events right behind the
    ///     parent
    ///     event in the event stream
    /// </summary>
    /// <param name="fanOutFunc"></param>
    /// <param name="mode">Should the fan out operation happen after grouping, or before? Default is after</param>
    /// <typeparam name="TEvent"></typeparam>
    /// <typeparam name="TChild"></typeparam>
    public MartenEventSlicer<TDoc, TId> FanOut<TEvent, TChild>(Func<IEvent<TEvent>, IEnumerable<TChild>> fanOutFunc, FanoutMode mode = FanoutMode.AfterGrouping)
    {
        return FanOut(new FanOutEventOperator<TEvent, TChild>(fanOutFunc) { Mode = mode }, mode);
    }

    private MartenEventSlicer<TDoc, TId> FanOut(IFanOutRule fanout, FanoutMode mode)
    {
        switch (mode)
        {
            case FanoutMode.AfterGrouping:
                _afterGroupingFanoutRules.Add(fanout);
                break;

            case FanoutMode.BeforeGrouping:
                _beforeGroupingFanoutRules.Add(fanout);
                break;
        }

        return this;
    }

    private async Task<JasperFx.Events.Grouping.EventSliceGroup<TDoc, TId>> groupSingleTenant(Tenant tenant, IQuerySession querySession,
        IList<IEvent> events)
    {
        var group = new JasperFx.Events.Grouping.EventSliceGroup<TDoc, TId>(tenant.TenantId);

        foreach (var grouper in _groupers) grouper.Apply(events, group);

        foreach (var lookupGrouper in _lookupGroupers)
            await lookupGrouper.Group(querySession, events, group).ConfigureAwait(false);

        group.ApplyFanOutRules(_afterGroupingFanoutRules);

        return group;
    }
}
