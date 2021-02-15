using System.Linq;
using Baseline;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class EqualsIgnoreCase_filtering: IntegrationContext
    {
        [Fact]
        public void can_search_case_insensitive()
        {
            var user1 = new User { UserName = "Abc" };
            var user2 = new User { UserName = "DeF" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user1, user2);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                #region sample_sample-linq-EqualsIgnoreCase
                query.Query<User>().Single(x => x.UserName.EqualsIgnoreCase("abc")).Id.ShouldBe(user1.Id);
                query.Query<User>().Single(x => x.UserName.EqualsIgnoreCase("aBc")).Id.ShouldBe(user1.Id);
                #endregion sample_sample-linq-EqualsIgnoreCase
                query.Query<User>().Single(x => x.UserName.EqualsIgnoreCase("def")).Id.ShouldBe(user2.Id);

                query.Query<User>().Any(x => x.UserName.EqualsIgnoreCase("abcd")).ShouldBeFalse();
            }
        }

        public EqualsIgnoreCase_filtering(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
