using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;

namespace EventSourcingTests.Examples;

public class Trip
{
    public Guid Id { get; set; }
    public DateTimeOffset Started { get; set; }
    public DateTimeOffset CorrelationId { get; set; }
    public string CustomerId { get; set; }
    public DateTimeOffset Ended { get; set; }
    public string Description { get; set; }

    public double TotalMiles { get; set; }

    public Guid InsuranceCompanyId { get; set; }
}

public class TripStarted
{
    public string Description { get; set; }
}
public class TripEnded{}

#region sample_aggregation_using_event_metadata

public class TripProjection: SingleStreamProjection<Trip, Guid>
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

    public override ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<Trip> slice)
    {
        // Emit other events or messages during asynchronous projection
        // processing

        // Access to the current state as of the projection
        // event page being processed *right* now
        var currentTrip = slice.Aggregate;

        if (currentTrip.TotalMiles > 1000)
        {
            // Append a new event to this stream
            slice.AppendEvent(new PassedThousandMiles());

            // Append a new event to a different event stream by
            // first specifying a different stream id
            slice.AppendEvent(currentTrip.InsuranceCompanyId, new IncrementThousandMileTrips());

            // "Publish" outgoing messages when the event page is successfully committed
            slice.PublishMessage(new SendCongratulationsOnLongTrip(currentTrip.Id));

            // And yep, you can make additional changes to Marten
            operations.Store(new CompletelyDifferentDocument
            {
                Name = "New Trip Segment",
                OriginalTripId = currentTrip.Id
            });
        }

        // This usage has to be async in case you're
        // doing any additional data access with the
        // Marten operations
        return new ValueTask();
    }
}

#endregion

public record PassedThousandMiles;

public record IncrementThousandMileTrips;

public record SendCongratulationsOnLongTrip(Guid TripId);

public class CompletelyDifferentDocument
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public Guid OriginalTripId { get; set; }
}
