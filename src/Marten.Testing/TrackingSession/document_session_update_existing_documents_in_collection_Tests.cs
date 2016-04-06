using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.TrackingSession
{
    public class document_session_update_existing_documents_in_collection_Tests
    {
        public class IdentityMapTests : DocumentSessionFixture<DirtyTrackingIdentityMap>
        {
            [Fact]
            public void when_querying_and_modifying_multiple_documents_should_track_and_persist()
            {
                var user1 = new User { FirstName = "James", LastName = "Worthy 1" };
                var user2 = new User { FirstName = "James", LastName = "Worthy 2" };
                var user3 = new User { FirstName = "James", LastName = "Worthy 3" };

                theSession.Store(user1, user2, user3);

                theSession.SaveChanges();

                using (var session2 = theStore.OpenSession(DocumentTracking.DirtyTracking))
                {
                    var users = session2.Query<User>().Where(x => x.FirstName == "James").ToList();

                    foreach (var user in users)
                    {
                        user.LastName += " - updated";
                    }

                    session2.SaveChanges();
                }

                using (var session2 = theStore.OpenSession())
                {
                    var users = session2.Query<User>().Where(x => x.FirstName == "James").OrderBy(x => x.LastName).ToList();

                    users.Select(x => x.LastName).ShouldHaveTheSameElementsAs("Worthy 1 - updated", "Worthy 2 - updated", "Worthy 3 - updated");
                }
            }
        }

        public class DirtyTrackingIdentityMapTests : DocumentSessionFixture<DirtyTrackingIdentityMap>
        {
            [Fact]
            public void when_querying_and_modifying_multiple_documents_should_track_and_persist()
            {
                var user1 = new User { FirstName = "James", LastName = "Worthy 1" };
                var user2 = new User { FirstName = "James", LastName = "Worthy 2" };
                var user3 = new User { FirstName = "James", LastName = "Worthy 3" };

                theSession.Store(user1);
                theSession.Store(user2);
                theSession.Store(user3);

                theSession.SaveChanges();

                using (var session2 = theStore.OpenSession(DocumentTracking.DirtyTracking))
                {
                    var users = session2.Query<User>().Where(x => x.FirstName == "James").ToList();

                    foreach (var user in users)
                    {
                        user.LastName += " - updated";
                    }

                    session2.SaveChanges();
                }

                using (var session2 = theStore.OpenSession())
                {
                    var users = session2.Query<User>().Where(x => x.FirstName == "James").OrderBy(x => x.LastName).ToList();

                    users.Select(x => x.LastName).ShouldHaveTheSameElementsAs("Worthy 1 - updated", "Worthy 2 - updated", "Worthy 3 - updated");
                }
            }
        }
    }
}