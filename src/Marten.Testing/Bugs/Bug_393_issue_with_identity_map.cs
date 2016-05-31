using System;
using Marten.Schema.Identity;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_393_issue_with_identity_map : DocumentSessionFixture<IdentityMap>
    {
        [Fact]
        public void load_non_existing_with_a_store_shoudl_return_new_added_document()
        {
            var routeId = CombGuidIdGeneration.NewGuid();

            using (var session = theStore.OpenSession())
            {
                var details = session.Load<RouteDetails>(routeId);
                details.ShouldBeNull();

                var routeDetails = new RouteDetails { Id = routeId };
                session.Store(routeDetails);

                details = session.Load<RouteDetails>(routeId); // this was always null

                details.ShouldBeTheSameAs(routeDetails);
            }
        }
    }

    public class Bug_393_issue_with_dirty_tracking_identity_map : DocumentSessionFixture<DirtyTrackingIdentityMap>
    {
        [Fact]
        public void load_non_existing_with_a_store_shoudl_return_new_added_document()
        {
            var routeId = CombGuidIdGeneration.NewGuid();

            using (var session = theStore.OpenSession())
            {
                var details = session.Load<RouteDetails>(routeId);
                details.ShouldBeNull();

                var routeDetails = new RouteDetails { Id = routeId };
                session.Store(routeDetails);

                details = session.Load<RouteDetails>(routeId);

                details.ShouldBeTheSameAs(routeDetails);
            }
        }
    }

    public class RouteDetails
    {
        public Guid Id { get; set; }
        public DateTime PlannedDate { get; set; }
        public DateTime PlannedSince { get; set; }
        public DateTime DrivingDate { get; set; }

        public string Status { get; set; }
    }
}