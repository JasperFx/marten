using System;
using System.Collections.Generic;
using System.Threading.Tasks.Dataflow;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using Marten.Events.Daemon.Internals;
using Marten.Storage;

namespace Marten.Events.Aggregation;

/// <summary>
///     Intermediate grouping of events by tenant within the asynchronous projection support. Really for aggregations
/// </summary>
/// <typeparam name="TDoc"></typeparam>
/// <typeparam name="TId"></typeparam>
public class TenantSliceGroup<TDoc, TId>: EventGrouping<TDoc, TId>
{
    public TenantSliceGroup(IQuerySession session, string tenantId): this(new Tenant(tenantId, session.Database))
    {
    }

    public TenantSliceGroup(Tenant tenant): base(tenant.TenantId)
    {
        Tenant = tenant;
    }

    public TenantSliceGroup(Tenant tenant, IEnumerable<EventSlice<TDoc, TId>> slices): this(tenant)
    {
        foreach (var slice in slices) Slices[slice.Id] = slice;
    }

    public Tenant Tenant { get; }

    public void Dispose()
    {
    }

    internal void ApplyFanOutRules(IReadOnlyList<IFanOutRule> rules)
    {
        foreach (var slice in Slices) slice.ApplyFanOutRules(rules);
    }

    [Obsolete("See if this can be eliminated")]
    public void Reset()
    {
        // Nothing
    }
}
