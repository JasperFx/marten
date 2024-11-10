#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using Marten.Events.Projections;
using Marten.Internal;
using Marten.Storage;

namespace Marten.Events.Aggregation;

/// <summary>
///     Slicer strategy by stream key (string identified streams)
/// </summary>
/// <typeparam name="TDoc"></typeparam>
public class ByStreamKey<TDoc>: IMartenEventSlicer<TDoc, string>, ISingleStreamSlicer<TDoc, string>
{
    public IReadOnlyList<EventSlice<TDoc, string>> Transform(IQuerySession querySession, IEnumerable<StreamAction> streams)
    {
        return streams.Select(s =>
        {
            var tenant = new Tenant(s.TenantId, querySession.Database);
            return new EventSlice<TDoc, string>(s.Key!, tenant.TenantId, s.Events) { ActionType = s.ActionType };
        }).ToList();
    }

    public ValueTask<IReadOnlyList<JasperFx.Events.Grouping.EventSliceGroup<TDoc, string>>> SliceAsyncEvents(
        IQuerySession querySession,
        List<IEvent> events)
    {
        var list = new List<JasperFx.Events.Grouping.EventSliceGroup<TDoc, string>>();
        var byTenant = events.GroupBy(x => x.TenantId);

        foreach (var tenantGroup in byTenant)
        {
            var tenant = new Tenant(tenantGroup.Key, querySession.Database);

            var slices = tenantGroup
                .GroupBy(x => x.StreamKey)
                .Select(x => new EventSlice<TDoc, string>(x.Key!, tenant.TenantId, x));

            var group = new JasperFx.Events.Grouping.EventSliceGroup<TDoc, string>(tenantGroup.Key, slices);

            list.Add(group);
        }

        return new ValueTask<IReadOnlyList<JasperFx.Events.Grouping.EventSliceGroup<TDoc, string>>>(list);
    }
}

/// <summary>
///     Slicer strategy by stream key (string identified streams) for strong typed identifiers
/// </summary>
/// <typeparam name="TDoc"></typeparam>
public class ByStreamKey<TDoc, TId>: IMartenEventSlicer<TDoc, TId>, ISingleStreamSlicer<TDoc, TId>
{
    private readonly Func<string,TId> _converter;

    public ByStreamKey(ValueTypeInfo valueType)
    {
        _converter = valueType.CreateConverter<TId, string>();
    }

    public IReadOnlyList<EventSlice<TDoc, TId>> Transform(IQuerySession querySession, IEnumerable<StreamAction> streams)
    {
        return streams.Select(s =>
        {
            var tenant = new Tenant(s.TenantId, querySession.Database);
            return new EventSlice<TDoc, TId>(_converter(s.Key!), tenant.TenantId, s.Events) { ActionType = s.ActionType };
        }).ToList();
    }

    public ValueTask<IReadOnlyList<JasperFx.Events.Grouping.EventSliceGroup<TDoc, TId>>> SliceAsyncEvents(
        IQuerySession querySession,
        List<IEvent> events)
    {
        var list = new List<JasperFx.Events.Grouping.EventSliceGroup<TDoc, TId>>();
        var byTenant = events.GroupBy(x => x.TenantId);

        foreach (var tenantGroup in byTenant)
        {
            var tenant = new Tenant(tenantGroup.Key, querySession.Database);

            var slices = tenantGroup
                .GroupBy(x => x.StreamKey)
                .Select(x => new EventSlice<TDoc, TId>(_converter(x.Key!), tenant.TenantId, x));

            var group = new JasperFx.Events.Grouping.EventSliceGroup<TDoc, TId>(tenantGroup.Key, slices);

            list.Add(group);
        }

        return new ValueTask<IReadOnlyList<JasperFx.Events.Grouping.EventSliceGroup<TDoc, TId>>>(list);
    }
}

