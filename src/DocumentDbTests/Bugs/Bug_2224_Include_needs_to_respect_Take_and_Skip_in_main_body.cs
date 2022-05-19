using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.Bugs
{
    public class Bug_2224_Include_needs_to_respect_Take_and_Skip_in_main_body : BugIntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public Bug_2224_Include_needs_to_respect_Take_and_Skip_in_main_body(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task include_to_list_using_inner_join_and_paging()
        {

            var user1 = new User();
            var user2 = new User();

            var issue1 = new Issue { AssigneeId = user1.Id, Title = "1.Garage Door is busted" };
            var issue2 = new Issue { AssigneeId = user2.Id, Title = "aaa. Garage Door is busted" };
            var issue3 = new Issue { AssigneeId = user2.Id, Title = "3. Garage Door is busted" };
            var issue4 = new Issue { AssigneeId = null, Title = "4. Garage Door is busted" };

            theSession.Store(user1, user2);
            theSession.Store(issue1, issue2, issue3, issue4);
            await theSession.SaveChangesAsync();

            await using var query = theStore.QuerySession();
            query.Logger = new TestOutputMartenLogger(_output);
            var list = new List<User>();

            var issues = await query.Query<Issue>()
                .Include<User>(x => x.AssigneeId, list)
                .Where(x => x.AssigneeId.HasValue)
                .OrderBy(x => x.Title)
                .Take(1)
                .ToListAsync();

            issues.Count().ShouldBe(1);
            list.Count.ShouldBe(1);
        }

        [Fact]
        public async Task include_to_list_using_inner_join_and_paging_and_ordering()
        {

            var user1 = new User();
            var user2 = new User();

            var issue1 = new Issue { AssigneeId = user1.Id, Title = "BBB.Garage Door is busted" };
            var issue2 = new Issue { AssigneeId = user2.Id, Title = "aaa. Garage Door is busted" };
            var issue3 = new Issue { AssigneeId = user2.Id, Title = "CCC. Garage Door is busted" };
            var issue4 = new Issue { AssigneeId = null, Title = "ddd. Garage Door is busted" };

            theSession.Store(user1, user2);
            theSession.Store(issue1, issue2, issue3, issue4);
            await theSession.SaveChangesAsync();

            await using var query = theStore.QuerySession();
            query.Logger = new TestOutputMartenLogger(_output);
            var list = new List<User>();

            var issues = await query.Query<Issue>()
                .Include<User>(x => x.AssigneeId, list)
                .Where(x => x.AssigneeId.HasValue)
                .OrderBy(x => x.Title)
                .Take(1)
                .ToListAsync();

            issues.Single().Title.ShouldBe(issue2.Title);
            list.Count.ShouldBe(1);
        }
    }
}
