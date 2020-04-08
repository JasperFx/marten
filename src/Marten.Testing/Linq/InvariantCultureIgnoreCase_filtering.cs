using System;
using System.Linq;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class InvariantCultureIgnoreCase_filtering: IntegratedFixture
    {
        [Fact]
        public void can_search_case_insensitive()
        {
            var user = new User {UserName = "TEST_USER"};

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                query.Query<User>().Single(x => x.UserName.Equals("test_user", StringComparison.InvariantCultureIgnoreCase)).Id.ShouldBe(user.Id);
            }
        }
    }
}
