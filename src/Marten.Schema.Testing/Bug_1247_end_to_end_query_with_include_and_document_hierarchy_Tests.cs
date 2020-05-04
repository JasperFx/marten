using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Schema.Testing.Documents;
using Marten.Schema.Testing.Hierarchies;
using Marten.Services;
using Marten.Services.Includes;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing
{
    public class Bug_1247_end_to_end_query_with_include_and_document_hierarchy_Tests: end_to_end_document_hierarchy_usage_Tests<IdentityMap>
    {
        [Fact]
        public void include_to_list_using_outer_join()
        {
            var user1 = new User();
            var user2 = new User();

            var issue1 = new Issue { AssigneeId = user1.Id, Title = "Garage Door is busted1" };
            var issue2 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted2" };
            var issue3 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted3" };
            var issue4 = new Issue { AssigneeId = null, Title = "Garage Door is busted4" };

            theSession.Store(user1, user2);
            theSession.Store(issue1, issue2, issue3, issue4);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var list = new List<User>();

                var issues = query.Query<Issue>().Include<User>(x => x.AssigneeId, list, JoinType.LeftOuter).ToArray();

                list.Count.ShouldBe(3);

                list.Any(x => x.Id == user1.Id);
                list.Any(x => x.Id == user2.Id);
                list.Any(x => x == null);

                issues.Length.ShouldBe(4);
            }
        }

        [Fact]
        public async Task include_to_list_using_outer_join_async()
        {
            var user1 = new User();
            var user2 = new User();

            var issue1 = new Issue { AssigneeId = user1.Id, Title = "Garage Door is busted1" };
            var issue2 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted2" };
            var issue3 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted3" };
            var issue4 = new Issue { AssigneeId = null, Title = "Garage Door is busted4" };

            theSession.Store(user1, user2);
            theSession.Store(issue1, issue2, issue3, issue4);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var list = new List<User>();

                var issues = await query.Query<Issue>().Include<User>(x => x.AssigneeId, list, JoinType.LeftOuter).ToListAsync();

                list.Count.ShouldBe(3);

                list.Any(x => x.Id == user1.Id).ShouldBeTrue();
                list.Any(x => x.Id == user2.Id).ShouldBeTrue();
                list.Any(x => x == null);

                issues.Count.ShouldBe(4);
            }
        }

    }
}
