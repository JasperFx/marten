using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Services;
using Samples.Deleting3;

namespace Marten.AsyncDaemon.Testing.TestingSupport
{
    internal class Samples
    {
        internal void register_projection()
        {
            #region sample_registering_an_aggregate_projection

            var store = DocumentStore.For(opts =>
            {
                opts.Connection("some connection string");

                // Register as inline
                opts.Projections.Add<TripProjection>(ProjectionLifecycle.Inline);

                // Or instead, register to run asynchronously
                opts.Projections.Add<TripProjection>(ProjectionLifecycle.Async);
            });

            #endregion
        }
    }

    public class TripAggregationWithCustomName: TripProjection
    {
        public TripAggregationWithCustomName()
        {
            ProjectionName = "Trip";
            TeardownDataOnRebuild = true;
        }
    }



    #region sample_TripProjection_aggregate

    public class TripProjection: SingleStreamAggregation<Trip>
    {
        public TripProjection()
        {
            DeleteEvent<TripAborted>();

            DeleteEvent<Breakdown>(x => x.IsCritical);

            DeleteEvent<VacationOver>((trip, v) => trip.Traveled > 1000);

            // Now let's change the lifecycle to inline
            Lifecycle = ProjectionLifecycle.Inline;
        }

        // These methods can be either public, internal, or private but there's
        // a small performance gain to making them public
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

    #endregion
}

namespace TripProjection.SelfAggregate
{
    internal class RegistrationSamples
    {
        #region sample_using_self_aggregate

        internal async Task use_a_self_aggregate()
        {
            var store = DocumentStore.For(opts =>
            {
                opts.Connection("some connection string");

                // Run the Trip as an inline projection
                opts.Projections.SelfAggregate<Trip>(ProjectionLifecycle.Inline);

                // Or run it as an asynchronous projection
                opts.Projections.SelfAggregate<Trip>(ProjectionLifecycle.Async);
            });

            // Or more likely, use it as a live aggregation:

            // Just pretend you already have the id of an existing
            // trip event stream id here...
            var tripId = Guid.NewGuid();

            // We'll open a read only query session...
            await using var session = store.QuerySession();

            // And do a live aggregation of the Trip stream
            var trip = await session.Events.AggregateStreamAsync<Trip>(tripId);
        }

        #endregion
    }


    #region sample_Trip_self_aggregate

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
        // event type
        internal Trip(TripStarted started)
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
        internal void Apply(Arrival e) => State = e.State;
        internal void Apply(Travel e) => Traveled += e.TotalDistance();
        internal void Apply(TripEnded e)
        {
            Active = false;
            EndedOn = e.Day;
        }

        // We think self-aggregates are mostly useful for live aggregations,
        // but hey, if you want to use a self aggregate as an asynchronous projection,
        // you can also specify when the aggregate document should be deleted
        internal bool ShouldDelete(TripAborted e) => true;
        internal bool ShouldDelete(Breakdown e) => e.IsCritical;
        internal bool ShouldDelete(VacationOver e) => Traveled > 1000;
    }

    #endregion
}


namespace TripProjection.UsingLambdas
{
    #region sample_using_ProjectEvent_in_aggregate_projection

    public class TripProjection: SingleStreamAggregation<Trip>
    {
        public TripProjection()
        {
            ProjectEvent<Arrival>((trip, e) => trip.State = e.State);
            ProjectEvent<Travel>((trip, e) => trip.Traveled += e.TotalDistance());
            ProjectEvent<TripEnded>((trip, e) =>
            {
                trip.Active = false;
                trip.EndedOn = e.Day;
            });

            ProjectEventAsync<Breakdown>(async (session, trip, e) =>
            {
                var repairShop = await session.Query<RepairShop>()
                    .Where(x => x.State == trip.State)
                    .FirstOrDefaultAsync();

                trip.RepairShopId = repairShop?.Id;
            });
        }
    }

    #endregion
}
