using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq.Compiled
{
    public class compiled_query_Tests: IntegrationContext
    {
        private readonly User _user1;
        private readonly User _user5;

        public compiled_query_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
            _user1 = new User { FirstName = "Jeremy", UserName = "jdm", LastName = "Miller" };
            var user2 = new User { FirstName = "Jens" };
            var user3 = new User { FirstName = "Jeff" };
            var user4 = new User { FirstName = "Corey", UserName = "myusername", LastName = "Kaylor" };
            _user5 = new User { FirstName = "Jeremy", UserName = "shadetreedev", LastName = "Miller" };

            theSession.Store(_user1, user2, user3, user4, _user5);
            theSession.SaveChanges();
        }

        [Fact]
        public void can_preview_command_for_a_compiled_query()
        {
            var cmd = theStore.Diagnostics.PreviewCommand(new UserByUsername { UserName = "hank" });

            cmd.CommandText.ShouldBe("select d.id, d.data from public.mt_doc_user as d where d.data ->> 'UserName' = :p0 LIMIT :p1");

            cmd.Parameters.First().Value.ShouldBe("hank");
        }

        [Fact]
        public void can_explain_the_plan_for_a_compiled_query()
        {
            var query = new UserByUsername { UserName = "hank" };

            var plan = theStore.Diagnostics.ExplainPlan(query);

            SpecificationExtensions.ShouldNotBeNull(plan);
        }

        [Fact]
        public void a_single_item_compiled_query()
        {
            var user = theSession.Query(new UserByUsername { UserName = "myusername" });
            user.ShouldNotBeNull();
            var differentUser = theSession.Query(new UserByUsername { UserName = "jdm" });
            differentUser.UserName.ShouldBe("jdm");
        }

        [Fact]
        public void a_single_item_compiled_query_with_fields()
        {
            var user = theSession.Query(new UserByUsernameWithFields { UserName = "myusername" });
            SpecificationExtensions.ShouldNotBeNull(user);
            var differentUser = theSession.Query(new UserByUsernameWithFields { UserName = "jdm" });
            differentUser.UserName.ShouldBe("jdm");
        }

        [Fact]
        public void a_single_item_compiled_query_SingleOrDefault()
        {

            var user = theSession.Query(new UserByUsernameSingleOrDefault() { UserName = "myusername" });
            user.ShouldNotBeNull();

            theSession.Query(new UserByUsernameSingleOrDefault() { UserName = "nonexistent" }).ShouldBeNull();
        }

        [Fact]
        public void a_filtered_list_compiled_query_AsJson()
        {
            var user = theSession.Query(new FindJsonUsersByUsername() { FirstName = "Jeremy" });

            SpecificationExtensions.ShouldNotBeNull(user);
            user.ShouldNotBeEmpty();
        }

        [Fact]
        public async Task a_filtered_list_compiled_query_AsJson_async()
        {
            var user = await theSession.QueryAsync(new FindJsonUsersByUsername() { FirstName = "Jeremy" });

            SpecificationExtensions.ShouldNotBeNull(user);
            user.ShouldNotBeEmpty();
        }

        [Fact]
        public void several_parameters_for_compiled_query()
        {
            var user = theSession.Query(new FindUserByAllTheThings { Username = "jdm", FirstName = "Jeremy", LastName = "Miller" });
            SpecificationExtensions.ShouldNotBeNull(user);
            user.UserName.ShouldBe("jdm");
            user = theSession.Query(new FindUserByAllTheThings { Username = "shadetreedev", FirstName = "Jeremy", LastName = "Miller" });
            SpecificationExtensions.ShouldNotBeNull(user);
            user.UserName.ShouldBe("shadetreedev");
        }

        [Fact]
        public async Task a_single_item_compiled_query_async()
        {
            var user = await theSession.QueryAsync(new UserByUsername { UserName = "myusername" });
            SpecificationExtensions.ShouldNotBeNull(user);
            var differentUser = await theSession.QueryAsync(new UserByUsername { UserName = "jdm" });
            differentUser.UserName.ShouldBe("jdm");
        }

        [Fact]
        public void a_list_query_compiled()
        {
            var users = theSession.Query(new UsersByFirstName { FirstName = "Jeremy" }).ToList();
            users.Count.ShouldBe(2);
            users.ElementAt(0).UserName.ShouldBe("jdm");
            users.ElementAt(1).UserName.ShouldBe("shadetreedev");
            var differentUsers = theSession.Query(new UsersByFirstName { FirstName = "Jeremy" });
            differentUsers.Count().ShouldBe(2);
        }

        [Fact]
        public void a_list_query_with_fields_compiled()
        {

            var users = theSession.Query(new UsersByFirstNameWithFields { FirstName = "Jeremy" }).ToList();
            users.Count.ShouldBe(2);
            users.ElementAt(0).UserName.ShouldBe("jdm");
            users.ElementAt(1).UserName.ShouldBe("shadetreedev");
            var differentUsers = theSession.Query(new UsersByFirstNameWithFields { FirstName = "Jeremy" });
            differentUsers.Count().ShouldBe(2);
        }

        [Fact]
        public async Task a_list_query_compiled_async()
        {
            var users = await theSession.QueryAsync(new UsersByFirstName { FirstName = "Jeremy" });
            users.Count().ShouldBe(2);
            users.ElementAt(0).UserName.ShouldBe("jdm");
            users.ElementAt(1).UserName.ShouldBe("shadetreedev");
            var differentUsers = await theSession.QueryAsync(new UsersByFirstName { FirstName = "Jeremy" });
            differentUsers.Count().ShouldBe(2);
        }

        [Fact]
        public void count_query_compiled()
        {
            var userCount = theSession.Query(new UserCountByFirstName { FirstName = "Jeremy" });
            userCount.ShouldBe(2);
            userCount = theSession.Query(new UserCountByFirstName { FirstName = "Corey" });
            userCount.ShouldBe(1);
        }

        [Fact]
        public void projection_query_compiled()
        {
            var user = theSession.Query(new UserProjectionToLoginPayload { UserName = "jdm" });
            user.ShouldNotBeNull();
            user.Username.ShouldBe("jdm");
            user = theSession.Query(new UserProjectionToLoginPayload { UserName = "shadetreedev" });
            user.ShouldNotBeNull();
            user.Username.ShouldBe("shadetreedev");
        }

        [Fact]
        public async Task bug_1090_Any_with_compiled_queries()
        {
            // Really just a smoke test now

            (await theSession.QueryAsync(new CompiledQuery1())).ShouldNotBeNull();
            (await theSession.QueryAsync(new CompiledQuery2())).ShouldNotBeNull();
        }

        [Fact]
        public async Task Bug_1623_use_any_within_compiled_query()
        {
            var user = new User {Age = 5, UserName = "testUser"};

            theSession.Store(user);
            await theSession.SaveChangesAsync();

            // this should pass => Any works.
            user.ShouldSatisfyAllConditions(
                () => theSession.Query<User>().Any(x => x.Age == 6).ShouldBeFalse(),
                () => theSession.Query<User>().Any(x => x.Age == 5).ShouldBeTrue()
            );

            // this should pass => AnyAsync works, too
            var asyncR1 = await theSession.Query<User>().AnyAsync(x => x.Age == 6);
            asyncR1.ShouldBeFalse();
            var asyncR2 = await theSession.Query<User>().AnyAsync(x => x.Age == 5);
            asyncR2.ShouldBeTrue();

            var q = new TestQuery() {Age = 6};
            var queryAsync = theSession.Query(q);  // theSession.QueryAsync(q, default) will fail also!
            queryAsync.ShouldBeFalse();
        }

        public class TestQuery: ICompiledQuery<User, bool>
        {
            public Expression<Func<IMartenQueryable<User>, bool>> QueryIs()
            {
                return query => query.Any(x => x.Age == Age);
            }

            public int Age { get; set; }
        }
    }

    #region sample_FindUserByAllTheThings
    public class FindUserByAllTheThings: ICompiledQuery<User>
    {
        public string Username { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }

        public Expression<Func<IMartenQueryable<User>, User>> QueryIs()
        {
            return query =>
                    query.Where(x => x.FirstName == FirstName && Username == x.UserName)
                        .Where(x => x.LastName == LastName)
                        .Single();
        }
    }

    #endregion sample_FindUserByAllTheThings

    #region sample_CompiledAsJson
    public class FindJsonUserByUsername: ICompiledQuery<User, string>
    {
        public string Username { get; set; }

        public Expression<Func<IMartenQueryable<User>, string>> QueryIs()
        {
            return query =>
                    query.Where(x => Username == x.UserName)
                        .AsJson().Single();
        }
    }

    #endregion sample_CompiledAsJson

    #region sample_CompiledToJsonArray
    public class FindJsonOrderedUsersByUsername: ICompiledQuery<User, string>
    {
        public string FirstName { get; set; }

        public Expression<Func<IMartenQueryable<User>, string>> QueryIs()
        {
            return query =>
                    query.Where(x => FirstName == x.FirstName)
                        .OrderBy(x => x.UserName)
                        .ToJsonArray();
        }
    }

    #endregion sample_CompiledToJsonArray

    public class FindJsonUsersByUsername: ICompiledQuery<User, string>
    {
        public string FirstName { get; set; }

        public Expression<Func<IMartenQueryable<User>, string>> QueryIs()
        {
            return query =>
                    query.Where(x => FirstName == x.FirstName)
                        .ToJsonArray();
        }
    }

    public class UserProjectionToLoginPayload: ICompiledQuery<User, LoginPayload>
    {
        public string UserName { get; set; }

        public Expression<Func<IMartenQueryable<User>, LoginPayload>> QueryIs()
        {
            return query => query.Where(x => x.UserName == UserName)
            .Select(x => new LoginPayload { Username = x.UserName }).Single();
        }
    }

    public class LoginPayload
    {
        public string Username { get; set; }
    }

    public class UserCountByFirstName: ICompiledQuery<User, int>
    {
        public string FirstName { get; set; }

        public Expression<Func<IMartenQueryable<User>, int>> QueryIs()
        {
            return query => query.Count(x => x.FirstName == FirstName);
        }
    }

    public class UserByUsername: ICompiledQuery<User>
    {
        public string UserName { get; set; }

        public Expression<Func<IMartenQueryable<User>, User>> QueryIs()
        {
            return query => query
                .FirstOrDefault(x => x.UserName == UserName);
        }
    }

    public class UserByUsernameWithFields: ICompiledQuery<User>
    {
        public string UserName;

        public Expression<Func<IMartenQueryable<User>, User>> QueryIs()
        {
            return query => Queryable.FirstOrDefault(query.Where(x => x.UserName == UserName));
        }
    }

    public class UserByUsernameSingleOrDefault: ICompiledQuery<User>
    {
        public static int Count;
        public string UserName { get; set; }

        public Expression<Func<IMartenQueryable<User>, User>> QueryIs()
        {
            Count++;
            return query => query.Where(x => x.UserName == UserName)
                .SingleOrDefault();
        }
    }

    #region sample_UsersByFirstName-Query
    public class UsersByFirstName: ICompiledListQuery<User>
    {
        public static int Count;
        public string FirstName { get; set; }

        public Expression<Func<IMartenQueryable<User>, IEnumerable<User>>> QueryIs()
        {
            return query => query.Where(x => x.FirstName == FirstName);
        }
    }

    #endregion sample_UsersByFirstName-Query

    public class UsersByFirstNameWithFields: ICompiledListQuery<User>
    {
        public string FirstName;

        public Expression<Func<IMartenQueryable<User>, IEnumerable<User>>> QueryIs()
        {
            return query => query.Where(x => x.FirstName == FirstName);
        }
    }

    #region sample_UserNamesForFirstName
    public class UserNamesForFirstName: ICompiledListQuery<User, string>
    {
        public Expression<Func<IMartenQueryable<User>, IEnumerable<string>>> QueryIs()
        {
            return q => q
                .Where(x => x.FirstName == FirstName)
                .Select(x => x.UserName);
        }

        public string FirstName { get; set; }
    }

    #endregion sample_UserNamesForFirstName

    public class CompiledQuery1 : ICompiledQuery<Target, bool>
    {
        public string StringValue { get; set; }

        public Expression<Func<IMartenQueryable<Target>, bool>> QueryIs()
        {
            return q => q.Any(x => x.String.EqualsIgnoreCase(StringValue));
        }
    }

    public class CompiledQuery2 : ICompiledQuery<Target, bool>
    {
        public Guid IdValue { get; set; }

        public Expression<Func<IMartenQueryable<Target>, bool>> QueryIs()
        {
            return q => q.Any(x => x.Id == IdValue);
        }
    }


}
