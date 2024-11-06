#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Projections;
using Marten.Internal;
using Marten.Storage;

namespace Marten.Events.Aggregation;

/// <summary>
///     Slicer strategy by stream key (string identified streams)
/// </summary>
/// <typeparam name="TDoc"></typeparam>
public class ByStreamKey<TDoc>: IEventSlicer<TDoc, string>, ISingleStreamSlicer<TDoc, string>
{
    public IReadOnlyList<EventSlice<TDoc, string>> Transform(IQuerySession querySession, IEnumerable<StreamAction> streams)
    {
        return streams.Select(s =>
        {
            var tenant = new Tenant(s.TenantId, querySession.Database);
            return new EventSlice<TDoc, string>(s.Key!, tenant.TenantId, s.Events) { ActionType = s.ActionType };
        }).ToList();
    }

    public ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, string>>> SliceAsyncEvents(
        IQuerySession querySession,
        List<IEvent> events)
    {
        var list = new List<TenantSliceGroup<TDoc, string>>();
        var byTenant = events.GroupBy(x => x.TenantId);

        foreach (var tenantGroup in byTenant)
        {
            var tenant = new Tenant(tenantGroup.Key, querySession.Database);

            var slices = tenantGroup
                .GroupBy(x => x.StreamKey)
                .Select(x => new EventSlice<TDoc, string>(x.Key!, tenant.TenantId, x));

            var group = new TenantSliceGroup<TDoc, string>(tenant, slices);

            list.Add(group);
        }

        return new ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, string>>>(list);
    }
}

/// <summary>
///     Slicer strategy by stream key (string identified streams) for strong typed identifiers
/// </summary>
/// <typeparam name="TDoc"></typeparam>
public class ByStreamKey<TDoc, TId>: IEventSlicer<TDoc, TId>, ISingleStreamSlicer<TDoc, TId>
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

    public ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> SliceAsyncEvents(
        IQuerySession querySession,
        List<IEvent> events)
    {
        var list = new List<TenantSliceGroup<TDoc, TId>>();
        var byTenant = events.GroupBy(x => x.TenantId);

        foreach (var tenantGroup in byTenant)
        {
            var tenant = new Tenant(tenantGroup.Key, querySession.Database);

            var slices = tenantGroup
                .GroupBy(x => x.StreamKey)
                .Select(x => new EventSlice<TDoc, TId>(_converter(x.Key!), tenant.TenantId, x));

            var group = new TenantSliceGroup<TDoc, TId>(tenant, slices);

            list.Add(group);
        }

        return new ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>>(list);
    }
}

