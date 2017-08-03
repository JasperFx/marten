using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity.Sequences
{
    public class IdentityKeyGenerationTests : IntegratedFixture
    {
        [Fact]
        public void When_documents_are_stored_after_each_other_then_the_first_id_should_be_less_than_the_second()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<UserWithString>().UseIdentityKey();
            });

            StoreUser(theStore, "User1");
            StoreUser(theStore, "User2");
            StoreUser(theStore, "User3");

            var users = GetUsers(theStore);

            GetId(users, "User1").ShouldBe("userwithstring/1");
            GetId(users, "User2").ShouldBe("userwithstring/2");
            GetId(users, "User3").ShouldBe("userwithstring/3");
        }

        private static string GetId(UserWithString[] users, string user1)
        {
            return users.Single(user => user.LastName == user1).Id;
        }

        private UserWithString[] GetUsers(IDocumentStore documentStore)
        {
            using (var session = documentStore.QuerySession())
            {
                return session.Query<UserWithString>().ToArray();
            }
        }

        private static void StoreUser(IDocumentStore documentStore, string lastName)
        {
            using (var session = documentStore.OpenSession())
            {
                session.Store(new UserWithString { LastName = lastName});
                session.SaveChanges();
            }
        }
    }
}