using JasperFx.Events.Aggregation;

namespace DcbLoadTest;

// ---------------------------------------------------------------------------
// Orders: the append / read / growth scenarios
// ---------------------------------------------------------------------------
public record OrderPlaced(string OrderId, decimal Amount);
public record OrderShipped(string TrackingNumber);
public record CustomerId(Guid Value);
public record RegionId(string Value);

[BoundaryAggregate]
public class OrderAggregate
{
    public int Placed { get; set; }
    public decimal TotalAmount { get; set; }
    public int ShipmentCount { get; set; }

    public void Apply(OrderPlaced e)
    {
        Placed++;
        TotalAmount += e.Amount;
    }

    public void Apply(OrderShipped e) => ShipmentCount++;
}

// ---------------------------------------------------------------------------
// Classes: the contended-decision scenarios, after the #5887 reporter's "join waitlist" shape.
// Every event is tagged with the class; the decision only folds ClassScheduled and SpotBooked.
// ---------------------------------------------------------------------------
public record ClassId(Guid Value);
public record ClassScheduled(Guid ClassId, int Capacity);
public record SpotBooked(Guid ClassId, Guid MemberId);
public record WaitlistJoined(Guid ClassId, Guid MemberId);

[BoundaryAggregate]
public class ClassState
{
    public int Capacity { get; set; }
    public int Booked { get; set; }

    public void Apply(ClassScheduled e) => Capacity = e.Capacity;
    public void Apply(SpotBooked e) => Booked++;
}
