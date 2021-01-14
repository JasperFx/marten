using Marten.Events.Aggregation;

namespace Marten.Testing.Events.Daemon.TestingSupport
{
    public class TripAggregation: AggregateProjection<Trip>
    {
        // TODO -- need to do something to create

        public TripAggregation()
        {
            DeleteEvent<TripAborted>();
        }

        public void Apply(Arrival e, Trip trip) => trip.State = e.State;
        public void Apply(Travel e, Trip trip) => trip.Traveled += e.TotalDistance();
        public void Apply(TripEnded e, Trip trip)
        {
            trip.Active = false;
            trip.EndedOn = e.Day;
        }

        public Trip Create(TripStarted started)
        {
            return new Trip {StartedOn = started.Day, Active = true};
        }
    }
}
