using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using DaemonTests.TestingSupport;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Samples.Deleting3;

namespace DaemonTests.TestingSupport
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

    public class TripProjectionWithCustomName: TripProjection
    {
        public TripProjectionWithCustomName()
        {
            ProjectionName = "TripCustomName";
            TeardownDataOnRebuild = true;
            Options.BatchSize = 5000;
        }
    }


    #region sample_TripProjection_aggregate

    public class TripProjection: SingleStreamProjection<Trip>
    {
        public TripProjection()
        {
            DeleteEvent<TripAborted>();

            DeleteEvent<Breakdown>(x => x.IsCritical);

            DeleteEvent<VacationOver>((trip, _) => trip.Traveled > 1000);
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
            return new Trip { StartedOn = started.Day, Active = true };
        }
    }

    #endregion
}

namespace TripProjection.StreamAggregation
{
    internal class RegistrationSamples
    {
        #region sample_using_stream_aggregation

        internal async Task use_a_stream_aggregation()
        {
            var store = DocumentStore.For(opts =>
            {
                opts.Connection("some connection string");

                // Run the Trip as an inline projection
                opts.Projections.Snapshot<Trip>(SnapshotLifecycle.Inline);

                // Or run it as an asynchronous projection
                opts.Projections.Snapshot<Trip>(SnapshotLifecycle.Async);
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


    #region sample_Trip_stream_aggregation

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

        // We think stream aggregation is mostly useful for live aggregations,
        // but hey, if you want to use a aggregation as an asynchronous projection,
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

    public class TripProjection: SingleStreamProjection<Trip>
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
