using System;

namespace Marten.Events.Projections
{
    [Obsolete("Please switch to MultiStreamAggregation<T> with the exact same syntax")]
    public abstract class ViewProjection<TDoc, TId> : MultiStreamAggregation<TDoc, TId>{}
}
