using System;
using EventSourcingTests.Examples;

namespace EventSourcingTests.Aggregation;

#region sample_OrderAggregate_with_version

public class OrderAggregate
{
    // This is most likely the stream id
    public Guid Id { get; set; }

    // This would be set automatically by Marten if
    // used as the target of a SingleStreamAggregation
    public int Version { get; set; }

    public void Apply(OrderShipped shipped) => HasShipped = true;
    public bool HasShipped { get; private set; }
}

#endregion
