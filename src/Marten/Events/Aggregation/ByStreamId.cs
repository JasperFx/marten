#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Events.Projections;
using Marten.Internal;
using Marten.Storage;

namespace Marten.Events.Aggregation;

public interface ISingleStreamSlicer
{

}

public interface ISingleStreamSlicer<TId> : ISingleStreamSlicer
{
    void ArchiveStream(IDocumentOperations operations, TId id);
}

/// <summary>
///     Slicer strategy by stream id (Guid identified streams)
/// </summary>
/// <typeparam name="TDoc"></typeparam>
public class ByStreamId<TDoc>: IEventSlicer<TDoc, Guid>, ISingleStreamSlicer<Guid>
{
    public void ArchiveStream(IDocumentOperations operations, Guid id)
    {
        operations.Events.ArchiveStream(id);
    }

    public ValueTask<IReadOnlyList<EventSlice<TDoc, Guid>>> SliceInlineActions(IQuerySession querySession,
        IEnumerable<StreamAction> streams)
    {
        return new ValueTask<IReadOnlyList<EventSlice<TDoc, Guid>>>(streams.Select(s =>
        {
            var tenant = new Tenant(s.TenantId, querySession.Database);
            return new EventSlice<TDoc, Guid>(s.Id, tenant, s.Events){ActionType = s.ActionType};
        }).ToList());
    }


    public ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, Guid>>> SliceAsyncEvents(IQuerySession querySession,
        List<IEvent> events)
    {
        var list = new List<TenantSliceGroup<TDoc, Guid>>();
        var byTenant = events.GroupBy(x => x.TenantId);

        foreach (var tenantGroup in byTenant)
        {
            var tenant = new Tenant(tenantGroup.Key, querySession.Database);

            var slices = tenantGroup
                .GroupBy(x => x.StreamId)
                .Select(x => new EventSlice<TDoc, Guid>(x.Key, tenant, x));

            var group = new TenantSliceGroup<TDoc, Guid>(tenant, slices);

            list.Add(group);
        }

        return new ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, Guid>>>(list);
    }
}

/// <summary>
///     Slicer strategy by stream id (Guid identified streams) and a custom value type
/// </summary>
/// <typeparam name="TDoc"></typeparam>
public class ByStreamId<TDoc, TId>: IEventSlicer<TDoc, TId>, ISingleStreamSlicer<TId>
{
    private readonly Func<Guid, TId> _converter;
    private readonly Func<TId,Guid> _unwrapper;

    public ByStreamId(ValueTypeInfo valueType)
    {
        _converter = valueType.CreateConverter<TId, Guid>();
        _unwrapper = valueType.ValueAccessor<TId, Guid>();
    }

    public void ArchiveStream(IDocumentOperations operations, TId id)
    {
        var streamId = _unwrapper(id);
        operations.Events.ArchiveStream(streamId);
    }

    public ValueTask<IReadOnlyList<EventSlice<TDoc, TId>>> SliceInlineActions(IQuerySession querySession,
        IEnumerable<StreamAction> streams)
    {
        return new ValueTask<IReadOnlyList<EventSlice<TDoc, TId>>>(streams.Select(s =>
        {
            var tenant = new Tenant(s.TenantId, querySession.Database);
            return new EventSlice<TDoc, TId>(_converter(s.Id), tenant, s.Events){ActionType = s.ActionType};
        }).ToList());
    }

    public ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> SliceAsyncEvents(IQuerySession querySession,
        List<IEvent> events)
    {
        var list = new List<TenantSliceGroup<TDoc, TId>>();
        var byTenant = events.GroupBy(x => x.TenantId);

        foreach (var tenantGroup in byTenant)
        {
            var tenant = new Tenant(tenantGroup.Key, querySession.Database);

            var slices = tenantGroup
                .GroupBy(x => x.StreamId)
                .Select(x => new EventSlice<TDoc, TId>( _converter(x.Key), tenant, x));

            var group = new TenantSliceGroup<TDoc, TId>(tenant, slices);

            list.Add(group);
        }

        return new ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>>(list);
    }
}

