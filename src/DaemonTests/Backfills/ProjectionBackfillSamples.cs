#nullable enable
using System;
using JasperFx.Events;
using Marten.Events.Aggregation;

namespace DaemonTests.Backfills.Samples;

public record OrderPlaced(string Customer, DateOnly Date);

public record OrderLineAdded(decimal Amount);

public class OrderSummary
{
    public Guid Id { get; set; }
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }

    // Both added after the projection was in production
    public int? Year { get; set; }
    public int LineCount { get; set; }
}

#region sample_declaring_a_projection_backfill
public class OrderSummaryProjection: SingleStreamProjection<OrderSummary, Guid>
{
    public OrderSummaryProjection()
    {
        // No version bump: the documents that already exist get Year and LineCount from this backfill
        Backfill("year-and-line-count")
            .On<OrderPlaced>((patch, e) => patch.Set(x => x.Year, e.Date.Year))
            .On<OrderLineAdded>((patch, _) => patch.Increment(x => x.LineCount));
    }

    // From here on the projection maintains both members itself
    public OrderSummary Create(IEvent<OrderPlaced> e) =>
        new() { Id = e.StreamId, Customer = e.Data.Customer, Year = e.Data.Date.Year };

    public void Apply(OrderSummary summary, OrderLineAdded e)
    {
        summary.Total += e.Amount;
        summary.LineCount++;
    }
}
#endregion
