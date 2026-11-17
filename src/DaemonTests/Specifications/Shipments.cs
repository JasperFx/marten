using System;
using Marten.Events.Aggregation;

namespace DaemonTests.Specifications;

public record ShipmentPacked(Guid Id, int Items);

public record ShipmentShipped(Guid Id);

public record ShipmentCancelled(Guid Id);

public class ShipmentSummary
{
    public Guid Id { get; set; }
    public int Items { get; set; }
    public bool Shipped { get; set; }
}

/// <summary>
/// The async projection the daemon specifications watch. Small on purpose: what is being specified is the
/// daemon, not the fold.
/// </summary>
public partial class ShipmentSummaryProjection: SingleStreamProjection<ShipmentSummary, Guid>
{
    public ShipmentSummary Create(ShipmentPacked e) => new() { Id = e.Id, Items = e.Items };

    public void Apply(ShipmentShipped e, ShipmentSummary summary) => summary.Shipped = true;

    public bool ShouldDelete(ShipmentCancelled e) => true;
}
