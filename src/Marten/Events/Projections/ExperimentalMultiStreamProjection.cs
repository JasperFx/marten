using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten.Events.Aggregation;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Events.Projections;

/// <summary>
///     Experimental option for doing multi-stream aggregations using more explicit code
///     for slicing
/// </summary>
/// <typeparam name="TDoc"></typeparam>
/// <typeparam name="TId"></typeparam>
public abstract class ExperimentalMultiStreamProjection<TDoc, TId>: GeneratedAggregateProjectionBase<TDoc>,
    IEventSlicer<TDoc, TId>
{
    private TenancyStyle _tenancyStyle;

    protected ExperimentalMultiStreamProjection(): base(AggregationScope.MultiStream)
    {
    }

    public virtual ValueTask<IReadOnlyList<EventSlice<TDoc, TId>>> SliceInlineActions(IQuerySession querySession,
        IEnumerable<StreamAction> streams)
    {
        throw new NotSupportedException("This projection can only be used with the Async lifecycle");
    }

    public ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> SliceAsyncEvents(
        IQuerySession querySession, List<IEvent> events)
    {
        return _tenancyStyle == TenancyStyle.Conjoined
            ? groupBySingleTenant(querySession, events)
            : groupByConjoinedTenancy(querySession, events);
    }

    protected override IEnumerable<string> validateDocumentIdentity(StoreOptions options, DocumentMapping mapping)
    {
        if (mapping.IdType != typeof(TId))
        {
            yield return
                $"Id type mismatch. The projection identity type is {typeof(TId).NameInCode()}, but the aggregate document {typeof(TId).FullNameInCode()} id type is {mapping.IdType.NameInCode()}";
        }
    }

    private async ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> groupByConjoinedTenancy(
        IQuerySession querySession, List<IEvent> events)
    {
        var byTenant = events.GroupBy(x => x.TenantId);

        var groupTasks = byTenant.Select(async tGroup =>
        {
            var tenant = new Tenant(tGroup.Key, querySession.Database);

            var tenantSession = querySession.ForTenant(tGroup.Key);
            var group = new TenantSliceGroup<TDoc, TId>(tenant);

            await GroupEvents(group, tenantSession, tGroup.ToList()).ConfigureAwait(false);

            return group;
        });

        var list = new List<TenantSliceGroup<TDoc, TId>>();
        foreach (var groupTask in groupTasks) list.Add(await groupTask.ConfigureAwait(false));

        return list;
    }

    private async ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> groupBySingleTenant(IQuerySession querySession,
        List<IEvent> events)
    {
        // This path is for *NOT* conjoined multi-tenanted projections, but we have to respect per-database tenancy
        var group = new TenantSliceGroup<TDoc, TId>(querySession, Tenancy.DefaultTenantId);
        await GroupEvents(group, querySession, events).ConfigureAwait(false);

        return new List<TenantSliceGroup<TDoc, TId>> { group };
    }

    protected abstract ValueTask GroupEvents(IEventGrouping<TId> grouping, IQuerySession session, List<IEvent> events);

    protected override object buildEventSlicer(StoreOptions options)
    {
        _tenancyStyle = options.Storage.MappingFor(typeof(TDoc)).TenancyStyle;

        return this;
    }

    protected override Type baseTypeForAggregationRuntime()
    {
        return typeof(AggregationRuntime<,>).MakeGenericType(typeof(TDoc), _aggregateMapping.IdType);
    }
}

[Obsolete("Please switch to ExperimentalMultiStreamProjection<TDoc, TId> with the exact same syntax")]
public abstract class ExperimentalMultiStreamAggregation<TDoc, TId>: ExperimentalMultiStreamProjection<TDoc, TId>
{
}
