using Marten.Events.Aggregation;
using Marten.Events.Projections;

namespace Marten.AsyncDaemon.Testing.TestingSupport
{
    public class TripAggregation: AggregateProjection<Trip>
    {
        public TripAggregation()
        {
            DeleteEvent<TripAborted>();

            ProjectionName = "Trip";

            // Now let's change the lifecycle to inline
            Lifecycle = ProjectionLifecycle.Inline;
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
