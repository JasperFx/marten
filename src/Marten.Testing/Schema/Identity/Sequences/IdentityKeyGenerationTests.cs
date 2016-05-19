using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity.Sequences
{
    public class IdentityKeyGenerationTests
    {
        [Fact]
        public void When_documents_are_stored_after_each_other_then_the_first_id_should_be_less_than_the_second()
        {
            using (
                var container =
                    ContainerFactory.Configure(options => options.DefaultIdStrategy = (mapping, storeOptions) => new IdentityKeyGeneration(mapping.As<DocumentMapping>(), storeOptions.HiloSequenceDefaults)))
            {
                container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();

                var store = container.GetInstance<IDocumentStore>();

                StoreUser(store, "User1");
                StoreUser(store, "User2");
                StoreUser(store, "User3");

                var users = GetUsers(store);

                GetId(users, "User1").ShouldBe("userwithstring/1");
                GetId(users, "User2").ShouldBe("userwithstring/2");
                GetId(users, "User3").ShouldBe("userwithstring/3");
            }
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
                session.Store(new UserWithString { LastName = lastName });
                session.SaveChanges();
            }
        }
    }
}