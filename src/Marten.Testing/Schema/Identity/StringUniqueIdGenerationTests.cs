using System;
using System.Linq;
using Baseline;
using Marten;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Testing.Schema.Identity.Sequences;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity
{
    public class StringUniqueIdGeneratorTests
    {
        [Fact]
        public void do_nothing_with_an_existing_value()
        {
			string existing = "someValue";

            var generator = new StringUniqueIdGeneration();

            bool assigned = true;

            generator.Assign(existing, out assigned).ShouldBe(existing);

            assigned.ShouldBeFalse();
        }

        [Fact]
        public void assign_a_new_value_with_an_empty()
        {
			string existing = string.Empty;
            var generator = new StringUniqueIdGeneration();
            bool assigned = false;
            generator.Assign(existing, out assigned)
                .ShouldNotBeNullOrEmpty();

            assigned.ShouldBeTrue();
        }

		[Fact]
		public void assign_a_new_value_with_an_null() {
			string existing = null;
			var generator = new StringUniqueIdGeneration();
			bool assigned = false;
			existing = generator.Assign(existing, out assigned);
			existing.ShouldNotBeNullOrEmpty();
			existing.Length.ShouldBe(20);

			assigned.ShouldBeTrue();
		}

		[Fact]
		public void store_documents() {
			using (
					var container =
							ContainerFactory.Configure(options => options.DefaultIdStrategy = (mapping, storeOptions) => new StringUniqueIdGeneration()  )) {
				container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();

				var store = container.GetInstance<IDocumentStore>();

				StoreUser(store, "User1");
				StoreUser(store, "User2");
				StoreUser(store, "User3");

				var users = GetUsers(store);
				foreach (UserWithString userWithString in users) {
					userWithString.Id.ShouldNotBeNullOrEmpty();
				}

			}
		}

		//private static string GetId(UserWithString[] users, string user1) {
		//	return users.Single(user => user.LastName == user1).Id;
		//}

		private UserWithString[] GetUsers(IDocumentStore documentStore) {
			using (var session = documentStore.QuerySession()) {
				return session.Query<UserWithString>().ToArray();
			}
		}

		private static void StoreUser(IDocumentStore documentStore, string lastName) {
			using (var session = documentStore.OpenSession()) {
				session.Store(new UserWithString { LastName = lastName });
				session.SaveChanges();
			}
		}

	}
}