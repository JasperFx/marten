using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class document_transforms : IntegratedFixture
    {
        public document_transforms()
        {
            StoreOptions(_ =>
            {
                _.Transforms.LoadFile("default_username.js");
            });
        }

        [Fact]
        public void transform_all_documents()
        {
            var user1 = new User {FirstName = "Jeremy", LastName = "Miller"};
            var user2 = new User {FirstName = "Corey", LastName = "Kaylor"};
            var user3 = new User {FirstName = "Tim", LastName = "Cools"};

            theStore.BulkInsert(new User[] {user1, user2, user3});

            theStore.Transform.All<User>("default_username");


            using (var session = theStore.QuerySession())
            {
                session.Load<User>(user1.Id).UserName.ShouldBe("jeremy.miller");
                session.Load<User>(user2.Id).UserName.ShouldBe("corey.kaylor");
                session.Load<User>(user3.Id).UserName.ShouldBe("tim.cools");
            }
        }

        [Fact]
        public void transform_a_single_document()
        {
            var user1 = new User { FirstName = "Jeremy", LastName = "Miller" };
            var user2 = new User { FirstName = "Corey", LastName = "Kaylor", UserName = "user2"};
            var user3 = new User { FirstName = "Tim", LastName = "Cools", UserName = "user3"};

            theStore.BulkInsert(new User[] { user1, user2, user3 });


            theStore.Transform.Document<User>("default_username", user1.Id);

            using (var session = theStore.QuerySession())
            {
                session.Load<User>(user1.Id).UserName.ShouldBe("jeremy.miller");

                // no updates to these
                session.Load<User>(user2.Id).UserName.ShouldBe("user2");
                session.Load<User>(user3.Id).UserName.ShouldBe("user3");
            }
        }

        [Fact]
        public void transform_with_where_clause()
        {
            var user1 = new User { FirstName = "Jeremy", LastName = "Miller" };
            var user2 = new User { FirstName = "Corey", LastName = "Kaylor", UserName = "user2" };
            var user3 = new User { FirstName = "Tim", LastName = "Cools", UserName = "user3" };

            theStore.BulkInsert(new User[] { user1, user2, user3 });


            theStore.Transform.Where<User>("default_username", x => x.FirstName == user1.FirstName);

            using (var session = theStore.QuerySession())
            {
                session.Load<User>(user1.Id).UserName.ShouldBe("jeremy.miller");

                // no updates to these
                session.Load<User>(user2.Id).UserName.ShouldBe("user2");
                session.Load<User>(user3.Id).UserName.ShouldBe("user3");
            }
        }
    }
}