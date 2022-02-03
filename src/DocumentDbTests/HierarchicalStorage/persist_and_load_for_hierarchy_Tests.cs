using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.HierarchicalStorage
{
    public class persist_and_load_for_hierarchy_Tests: end_to_end_document_hierarchy_usage_Tests
    {
        public persist_and_load_for_hierarchy_Tests()
        {
            DocumentTracking = DocumentTracking.IdentityOnly;
        }

        [Fact]
        public void persist_and_delete_subclass()
        {
            theSession.Store(admin1);
            theSession.SaveChanges();

            theSession.Delete(admin1);

            theSession.SaveChanges();

            theSession.Load<User>(admin1.Id).ShouldBeNull();
            theSession.Load<AdminUser>(admin1.Id).ShouldBeNull();
        }


        [Fact]
        public void persist_and_delete_subclass_2()
        {
            theSession.Store(admin1);
            theSession.SaveChanges();

            theSession.Delete<AdminUser>(admin1.Id);

            theSession.SaveChanges();

            theSession.Load<User>(admin1.Id).ShouldBeNull();
            theSession.Load<AdminUser>(admin1.Id).ShouldBeNull();
        }

        [Fact]
        public void persist_and_delete_top()
        {
            theSession.Store(user1);
            theSession.SaveChanges();

            theSession.Delete<User>(user1.Id);
            theSession.SaveChanges();

            theSession.Load<User>(user1.Id).ShouldBeNull();
        }

        [Fact]
        public void persist_and_delete_top_2()
        {
            theSession.Store(user1);
            theSession.SaveChanges();

            theSession.Delete(user1);
            theSession.SaveChanges();

            theSession.Load<User>(user1.Id).ShouldBeNull();
        }


        [Fact]
        public void persist_and_load_subclass()
        {
            theSession.Store(admin1);
            theSession.SaveChanges();

            theSession.Load<User>(admin1.Id).ShouldBeTheSameAs(admin1);
            theSession.Load<AdminUser>(admin1.Id).ShouldBeTheSameAs(admin1);

            using (var session = theStore.QuerySession())
            {
                session.Load<AdminUser>(admin1.Id).ShouldNotBeTheSameAs(admin1).ShouldNotBeNull();
                session.Load<User>(admin1.Id).ShouldNotBeTheSameAs(admin1).ShouldNotBeNull();
            }
        }

        [Fact]
        public async Task persist_and_load_subclass_async()
        {
            theSession.Store(admin1);
            theSession.SaveChanges();

            (await theSession.LoadAsync<User>(admin1.Id)).ShouldBeTheSameAs(admin1);
            (await theSession.LoadAsync<AdminUser>(admin1.Id)).ShouldBeTheSameAs(admin1);

            using (var session = theStore.QuerySession())
            {
                (await session.LoadAsync<AdminUser>(admin1.Id)).ShouldNotBeTheSameAs(admin1)
                    .ShouldNotBeNull();
                (await session.LoadAsync<User>(admin1.Id)).ShouldNotBeTheSameAs(admin1)
                    .ShouldNotBeNull();
            }
        }

        [Fact]
        public void persist_and_load_top_level()
        {
            theSession.Store(user1);
            theSession.SaveChanges();

            theSession.Load<User>(user1.Id).ShouldBeTheSameAs(user1);

            using (var session = theStore.QuerySession())
            {
                session.Load<User>(user1.Id).ShouldNotBeTheSameAs(user1).ShouldNotBeNull();
            }
        }

    }
}