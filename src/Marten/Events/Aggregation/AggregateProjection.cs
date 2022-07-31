using System;

namespace Marten.Events.Aggregation
{
    [Obsolete("Please switch to SingleStreamAggregation<T> with the exact same syntax")]
    public class AggregateProjection<T> : SingleStreamAggregation<T>{}
}
