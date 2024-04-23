using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using DaemonTests.TestingSupport;
using Marten.Events.Aggregation;

namespace DaemonTests
{

}

namespace Samples.Deleting
{
    #region sample_deleting_aggregate_by_event_type

    public class TripProjection: SingleStreamProjection<Trip>
    {
        public TripProjection()
        {
            // The current Trip aggregate would be deleted if
            // the projection encountered a TripAborted event
            DeleteEvent<TripAborted>();
        }
    }

    #endregion
}


namespace Samples.Deleting2
{
    public class RepairShop
    {
        public string State { get; set; }
        public Guid Id { get; set; }
    }

    #region sample_deleting_aggregate_by_event_type_and_func

    public class TripProjection: SingleStreamProjection<Trip>
    {
        public TripProjection()
        {
            // The current Trip aggregate would be deleted if
            // the Breakdown event is "critical"
            DeleteEvent<Breakdown>(x => x.IsCritical);

            // Alternatively, delete the aggregate if the trip
            // is currently in New Mexico and the breakdown is critical
            DeleteEvent<Breakdown>((trip, e) => e.IsCritical && trip.State == "New Mexico");

            DeleteEventAsync<Breakdown>(async (session, trip, e) =>
            {
                var anyRepairShopsInState = await session.Query<RepairShop>()
                    .Where(x => x.State == trip.State)
                    .AnyAsync();

                // Delete the trip if there are no repair shops in
                // the current state
                return !anyRepairShopsInState;
            });
        }
    }

    #endregion


}


namespace Samples.Deleting3
{
    public class RepairShop
    {
        public string State { get; set; }
        public Guid Id { get; set; }
    }

    #region sample_deleting_aggregate_by_event_type_and_func_with_convention

    public class TripProjection: SingleStreamProjection<Trip>
    {
        // The current Trip aggregate would be deleted if
        // the Breakdown event is "critical"
        public bool ShouldDelete(Breakdown breakdown) => breakdown.IsCritical;

        // Alternatively, delete the aggregate if the trip
        // is currently in New Mexico and the breakdown is critical
        public bool ShouldDelete(Trip trip, Breakdown breakdown)
            => breakdown.IsCritical && trip.State == "New Mexico";

        public async Task<bool> ShouldDelete(IQuerySession session, Trip trip, Breakdown breakdown)
        {
            var anyRepairShopsInState = await session.Query<RepairShop>()
                .Where(x => x.State == trip.State)
                .AnyAsync();

            // Delete the trip if there are no repair shops in
            // the current state
            return !anyRepairShopsInState;
        }
    }

    #endregion


}

