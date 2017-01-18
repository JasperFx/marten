using System;
using System.Linq;
using Marten.Linq.LastModified;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class last_modified_queries : IntegratedFixture
    {
        [Fact]
        public void query_modified_since_docs()
        {
            var user1 = new User { UserName = "foo" };
            var user2 = new User { UserName = "bar" };
            var user3 = new User { UserName = "baz" };
            var user4 = new User { UserName = "jack" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user1, user2, user3, user4);
                session.SaveChanges();

                var beforeChange = DateTimeOffset.UtcNow.DateTime;
                session.Store(user3, user4);
                session.SaveChanges();

                // no where clause
                session.Query<User>().Where(x => x.ModifiedSince(beforeChange)).OrderBy(x => x.UserName).Select(x => x.UserName)
                    .ToList().ShouldHaveTheSameElementsAs("baz", "jack");

                // with a where clause
                session.Query<User>().Where(x => x.UserName != "baz" && x.ModifiedSince(beforeChange))
                    .OrderBy(x => x.UserName)
                    .ToList()
                    .Select(x => x.UserName)
                    .Single().ShouldBe("jack");
            }
        }

        [Fact]
        public void query_modified_before_docs()
        {
            var user1 = new User { UserName = "foo" };
            var user2 = new User { UserName = "bar" };
            var user3 = new User { UserName = "baz" };
            var user4 = new User { UserName = "jack" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user1, user2, user3, user4);
                session.SaveChanges();

                var epoch = DateTimeOffset.UtcNow;
                session.Store(user3, user4);
                session.SaveChanges();

                // no where clause
                session.Query<User>().Where(x => x.ModifiedBefore(epoch)).OrderBy(x => x.UserName).Select(x => x.UserName)
                    .ToList().ShouldHaveTheSameElementsAs("bar", "foo");

                // with a where clause
                session.Query<User>().Where(x => x.UserName != "bar" && x.ModifiedBefore(epoch))
                    .OrderBy(x => x.UserName)
                    .ToList()
                    .Select(x => x.UserName)
                    .Single().ShouldBe("foo");
            }
        }
    }
}
