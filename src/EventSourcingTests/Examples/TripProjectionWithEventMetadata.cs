using System;
using Marten.Events;
using Marten.Events.Aggregation;

namespace EventSourcingTests.Examples;

public class Trip
{
    public Guid Id { get; set; }
    public DateTimeOffset Started { get; set; }
    public DateTimeOffset CorrelationId { get; set; }
    public string CustomerId { get; set; }
    public DateTimeOffset Ended { get; set; }
    public string Description { get; set; }
}

public class TripStarted
{
    public string Description { get; set; }
}
public class TripEnded{}

#region sample_aggregation_using_event_metadata

public class TripProjection: SingleStreamAggregation<Trip>
{
    // Access event metadata through IEvent<T>
    public Trip Create(IEvent<TripStarted> @event)
    {
        var trip = new Trip
        {
            Id = @event.StreamId, // Marten does this for you anyway
            Started = @event.Timestamp,
            CorrelationId = @event.Timestamp, // Open telemetry type tracing
            Description = @event.Data.Description // Still access to the event body
        };

        // Use a custom header
        if (@event.Headers.TryGetValue("customer", out var customerId))
        {
            trip.CustomerId = (string)customerId;
        }

        return trip;
    }

    public void Apply(TripEnded ended, Trip trip, IEvent @event)
    {
        trip.Ended = @event.Timestamp;
    }

    // Other Apply/ShouldDelete methods
}

#endregion
