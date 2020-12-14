using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq.Compiled
{
    public class compiled_query_by_string_fragments : IntegrationContext
    {
        public compiled_query_by_string_fragments(DefaultStoreFixture fixture) : base(fixture)
        {


        }

        [Fact]
        public async Task can_use_starts_with()
        {
            var user1 = new User
            {
                UserName = "Sir Gawain"
            };

            var user2 = new User {UserName = "Sir Mixalot"};

            var user3 = new User {UserName = "Ser Gregor"};

            var user4 = new User {UserName = "Von Miller"};
            var user5 = new User {UserName = "Otto Von Bismark"};

            theSession.Store(user1, user2, user3, user4, user5);
            await theSession.SaveChangesAsync();

            var users = await theSession.QueryAsync(new UserNameStartsWith());
            users.OrderBy(x => x.UserName).Select(x => x.UserName)
                .ShouldHaveTheSameElementsAs("Sir Gawain", "Sir Mixalot");
        }

        public class UserNameStartsWith: ICompiledListQuery<User>
        {
            public string Prefix { get; set; } = "Sir";

            public Expression<Func<IMartenQueryable<User>, IEnumerable<User>>> QueryIs()
            {
                return q => q.Where(x => x.UserName.StartsWith(Prefix));
            }
        }

        [Fact]
        public async Task can_use_ends_with()
        {
            var user1 = new User
            {
                UserName = "Sir Gawain"
            };

            var user2 = new User {UserName = "Sir Mixalot"};

            var user3 = new User {UserName = "Ser Gregor"};

            var user4 = new User {UserName = "Von Miller"};
            var user5 = new User {UserName = "Gary Clark Jr"};

            theSession.Store(user1, user2, user3, user4, user5);
            await theSession.SaveChangesAsync();

            var users = await theSession.QueryAsync(new UserNameEndsWith());
            users.Single().UserName.ShouldBe("Gary Clark Jr");
        }

        public class UserNameEndsWith: ICompiledListQuery<User>
        {
            public string Suffix { get; set; } = "Jr";

            public Expression<Func<IMartenQueryable<User>, IEnumerable<User>>> QueryIs()
            {
                return q => q.Where(x => x.UserName.EndsWith(Suffix));
            }
        }

        [Fact]
        public async Task can_use_string_contains()
        {
            var user1 = new User
            {
                UserName = "Sir Gawain"
            };

            var user2 = new User {UserName = "Sir Mixalot"};

            var user3 = new User {UserName = "Ser Gregor"};

            var user4 = new User {UserName = "Von Miller"};
            var user5 = new User {UserName = "Otto Von Bismark"};

            theSession.Store(user1, user2, user3, user4, user5);
            await theSession.SaveChangesAsync();

            var users = await theSession.QueryAsync(new UserNameContains());
            users.OrderBy(x => x.UserName).Select(x => x.UserName)
                .ShouldHaveTheSameElementsAs("Otto Von Bismark", "Von Miller");
        }

        public class UserNameContains: ICompiledListQuery<User>
        {
            public string Fragment { get; set; } = "Von";

            public Expression<Func<IMartenQueryable<User>, IEnumerable<User>>> QueryIs()
            {
                return q => q.Where(x => x.UserName.Contains(Fragment));
            }
        }

        [Fact]
        public async Task can_use_string_contains_case_insensitive()
        {
            var user1 = new User
            {
                UserName = "Sir Gawain"
            };

            var user2 = new User {UserName = "Sir Mixalot"};

            var user3 = new User {UserName = "Ser Gregor"};

            var user4 = new User {UserName = "Von Miller"};
            var user5 = new User {UserName = "Otto von Bismark"};

            theSession.Store(user1, user2, user3, user4, user5);
            await theSession.SaveChangesAsync();

            var users = await theSession.QueryAsync(new UserNameContainsInsensitive());
            users.OrderBy(x => x.UserName).Select(x => x.UserName)
                .ShouldHaveTheSameElementsAs("Otto von Bismark", "Von Miller");
        }

        public class UserNameContainsInsensitive: ICompiledListQuery<User>
        {
            public string Fragment { get; set; } = "Von";

            public Expression<Func<IMartenQueryable<User>, IEnumerable<User>>> QueryIs()
            {
                return q => q.Where(x => x.UserName.Contains(Fragment, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
