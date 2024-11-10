#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using Marten.Events.Projections;
using Marten.Internal;
using Marten.Storage;

namespace Marten.Events.Aggregation;

public interface ISingleStreamSlicer{}

public interface ISingleStreamSlicer<TDoc, TId> : ISingleStreamSlicer
{
    IReadOnlyList<EventSlice<TDoc, TId>> Transform(IQuerySession querySession,
        IEnumerable<StreamAction> streams);
}

/// <summary>
///     Slicer strategy by stream id (Guid identified streams)
/// </summary>
/// <typeparam name="TDoc"></typeparam>
public class ByStreamId<TDoc>: IMartenEventSlicer<TDoc, Guid>, ISingleStreamSlicer<TDoc, Guid>
{
    public IReadOnlyList<EventSlice<TDoc, Guid>> Transform(IQuerySession querySession, IEnumerable<StreamAction> streams)
    {
        return streams.Select(s =>
        {
            var tenant = new Tenant(s.TenantId, querySession.Database);
            return new EventSlice<TDoc, Guid>(s.Id, tenant.TenantId, s.Events) { ActionType = s.ActionType };
        }).ToList();
    }


    public ValueTask<IReadOnlyList<JasperFx.Events.Grouping.EventSliceGroup<TDoc, Guid>>> SliceAsyncEvents(
        IQuerySession querySession,
        List<IEvent> events)
    {
        var list = new List<JasperFx.Events.Grouping.EventSliceGroup<TDoc, Guid>>();
        var byTenant = events.GroupBy(x => x.TenantId);

        foreach (var tenantGroup in byTenant)
        {
            var tenant = new Tenant(tenantGroup.Key, querySession.Database);

            var slices = tenantGroup
                .GroupBy(x => x.StreamId)
                .Select(x => new EventSlice<TDoc, Guid>(x.Key, tenant.TenantId, x));

            var group = new JasperFx.Events.Grouping.EventSliceGroup<TDoc, Guid>(tenantGroup.Key, slices);

            list.Add(group);
        }

        return new ValueTask<IReadOnlyList<JasperFx.Events.Grouping.EventSliceGroup<TDoc, Guid>>>(list);
    }
}

/// <summary>
///     Slicer strategy by stream id (Guid identified streams) and a custom value type
/// </summary>
/// <typeparam name="TDoc"></typeparam>
public class ByStreamId<TDoc, TId>: IMartenEventSlicer<TDoc, TId>, ISingleStreamSlicer<TDoc, TId>
{
    private readonly Func<Guid, TId> _converter;

    public ByStreamId(ValueTypeInfo valueType)
    {
        _converter = valueType.CreateConverter<TId, Guid>();
    }

    public IReadOnlyList<EventSlice<TDoc, TId>> Transform(IQuerySession querySession, IEnumerable<StreamAction> streams)
    {
        return streams.Select(s =>
        {
            var tenant = new Tenant(s.TenantId, querySession.Database);
            return new EventSlice<TDoc, TId>(_converter(s.Id), tenant.TenantId, s.Events) { ActionType = s.ActionType };
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
                .GroupBy(x => x.StreamId)
                .Select(x => new EventSlice<TDoc, TId>( _converter(x.Key), tenant.TenantId, x));

            var group = new JasperFx.Events.Grouping.EventSliceGroup<TDoc, TId>(tenantGroup.Key, slices);

            list.Add(group);
        }

        return new ValueTask<IReadOnlyList<JasperFx.Events.Grouping.EventSliceGroup<TDoc, TId>>>(list);
    }
}

