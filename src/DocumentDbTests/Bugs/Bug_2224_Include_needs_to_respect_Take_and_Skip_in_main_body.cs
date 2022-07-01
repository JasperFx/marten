using System;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Pagination;
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

        [Fact]
        public async Task Bug_2258_get_all_related_documents()
        {
            var tenant1 = new Tenant2();
            var tenant2 = new Tenant2();
            var tenant3 = new Tenant2();

            theSession.Store(tenant1, tenant2, tenant3);

            await theSession.SaveChangesAsync();

            var user1 = new User2 { TenantIds = new List<Guid> { tenant1.Id, tenant2.Id, tenant3.Id } };
            theSession.Store(user1);
            await theSession.SaveChangesAsync();

            theSession.Logger = new TestOutputMartenLogger(_output);

            var tenants = new Dictionary<Guid, Tenant2>();
            var user = await theSession
                .Query<User2>()
                .Include(x => x.TenantIds, tenants)
                .SingleOrDefaultAsync(x => x.Id == user1.Id);

            user.Id.ShouldBe(user1.Id);
            tenants.Count.ShouldBe(3);
            tenants.ContainsKey(tenant1.Id).ShouldBeTrue();
            tenants.ContainsKey(tenant2.Id).ShouldBeTrue();
            tenants.ContainsKey(tenant3.Id).ShouldBeTrue();
        }

        [Fact]
        public async Task include_with_pagination()
        {
            var targets = Target.GenerateRandomData(80).ToArray();
            var users = targets.Select(target =>
            {
                return new TargetUser
                {
                    TargetId = target.Id, Number = target.Number // this is random anyway
                };
            }).ToArray();

            await theStore.BulkInsertAsync(targets);
            await theStore.BulkInsertAsync(users);

            theSession.Logger = new TestOutputMartenLogger(_output);

            var dict = new Dictionary<Guid, Target>();
            var records = await theSession.Query<TargetUser>()
                .Include(x => x.TargetId, dict)
                .OrderBy(x => x.Number)
                .ToPagedListAsync(3, 10);

            records.Count.ShouldBe(10);
            records.PageCount.ShouldBe(8);
            records.PageNumber.ShouldBe(3);
            records.TotalItemCount.ShouldBe(80);

            dict.Count.ShouldBe(10);

            foreach (var targetUser in records)
            {
                dict.ContainsKey(targetUser.TargetId).ShouldBeTrue();
            }
        }

        [Fact]
        public async Task Bug_2243_include_to_list_using_inner_join_and_paging()
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

            var list = new List<User>();

            QueryStatistics stats;
            var issues = await query.Query<Issue>()
                .Stats(out stats)
                .Include<User>(x => x.AssigneeId, list)
                .Where(x => x.AssigneeId.HasValue)
                .OrderBy(x => x.Title)
                .Take(1)
                .ToListAsync();

            issues.Count().ShouldBe(1);
            list.Count.ShouldBe(1);
            stats.TotalResults.ShouldBe(3);
        }
    }

    public class TargetUser
    {
        public Guid Id { get; set; }
        public Guid TargetId { get; set; }

        public int Number { get; set; }
    }

    public class User2
    {
        public Guid Id { get; set; }
        public List<Guid> TenantIds { get; set; }
    }

    public class Tenant2
    {
        public Guid Id { get; set; }
    }
}
