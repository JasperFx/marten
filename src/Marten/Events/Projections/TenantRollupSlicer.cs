#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Aggregation;

namespace Marten.Events.Projections;

[Obsolete("get this to JasperFx")]
public class TenantRollupSlicer<TDoc>: IMartenEventSlicer<TDoc, string>
{
    public ValueTask<IReadOnlyList<JasperFx.Events.Grouping.EventSliceGroup<TDoc, string>>> SliceAsyncEvents(
        IQuerySession querySession, List<IEvent> events)
    {
        var sliceGroup = new JasperFx.Events.Grouping.EventSliceGroup<TDoc, string>();
        var groups = events.GroupBy(x => x.TenantId);
        foreach (var @group in groups)
        {
            sliceGroup.AddEvents(@group.Key, @group);
        }

        var list = new List<JasperFx.Events.Grouping.EventSliceGroup<TDoc, string>>{sliceGroup};

        return ValueTask.FromResult<IReadOnlyList<JasperFx.Events.Grouping.EventSliceGroup<TDoc, string>>>(list);
    }
}
