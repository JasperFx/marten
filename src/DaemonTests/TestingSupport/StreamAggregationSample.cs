using System;

namespace DaemonTests.TestingSupport.StreamAggregationSample;

// The documentation sample for a self-aggregating Trip. It lives apart from the Trip that TripProjection aggregates,
// because the source generator also looks for conventional methods on the aggregate type, and public methods on that
// Trip would be applied by TripProjection as well.

#region sample_trip_stream_aggregation

public class Trip
{
    // Probably safest to have an empty, default
    // constructor unless you can guarantee that
    // a certain event type will always be first in
    // the event stream
    public Trip()
    {
    }

    // Create a new aggregate based on the initial
    // event type. Like every conventional method,
    // it must be public
    public Trip(TripStarted started)
    {
        StartedOn = started.Day;
        Active = true;
    }

    public Guid Id { get; set; }
    public int EndedOn { get; set; }

    public double Traveled { get; set; }

    public string State { get; set; }

    public bool Active { get; set; }

    public int StartedOn { get; set; }
    public Guid? RepairShopId { get; set; }

    // The Apply() methods would mutate the aggregate state
    public void Apply(Arrival e) => State = e.State;
    public void Apply(Travel e) => Traveled += e.TotalDistance();

    public void Apply(TripEnded e)
    {
        Active = false;
        EndedOn = e.Day;
    }

    // We think stream aggregation is mostly useful for live aggregations,
    // but hey, if you want to use a aggregation as an asynchronous projection,
    // you can also specify when the aggregate document should be deleted
    public bool ShouldDelete(TripAborted e) => true;
    public bool ShouldDelete(Breakdown e) => e.IsCritical;
    public bool ShouldDelete(VacationOver e) => Traveled > 1000;
}

#endregion
