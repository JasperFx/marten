using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq.Compiled
{
    public class compiled_query_Tests: IntegrationContextWithIdentityMap<NulloIdentityMap>
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

            cmd.CommandText.ShouldBe("select d.data, d.id, d.mt_version from public.mt_doc_user as d where d.data ->> 'UserName' = :arg0 LIMIT 1");

            cmd.Parameters.Single().Value.ShouldBe("hank");
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
            SpecificationExtensions.ShouldNotBeNull(user);
            var differentUser = theSession.Query(new UserByUsername { UserName = "jdm" });
            differentUser.UserName.ShouldBe("jdm");
        }

        [Fact]
        public void a_single_item_compiled_query_with_fields()
        {
            UserByUsernameWithFields.Count = 0;

            var user = theSession.Query(new UserByUsernameWithFields { UserName = "myusername" });
            SpecificationExtensions.ShouldNotBeNull(user);
            var differentUser = theSession.Query(new UserByUsernameWithFields { UserName = "jdm" });
            differentUser.UserName.ShouldBe("jdm");
            UserByUsernameWithFields.Count.ShouldBe(1);
        }

        [Fact]
        public void a_single_item_compiled_query_SingleOrDefault()
        {
            UserByUsername.Count = 0;

            var user = theSession.Query(new UserByUsernameSingleOrDefault() { UserName = "myusername" });
            SpecificationExtensions.ShouldNotBeNull(user);

            SpecificationExtensions.ShouldBeNull(theSession.Query(new UserByUsernameSingleOrDefault() { UserName = "nonexistent" }));
        }

        // SAMPLE: FindJsonUserByUsername
        [Fact]
        public void a_single_item_compiled_query_AsJson()
        {
            var user = theSession.Query(new FindJsonUserByUsername() { Username = "jdm" });

            SpecificationExtensions.ShouldNotBeNull(user);
            user.ShouldBe(_user1.ToJson());
        }

        // ENDSAMPLE

        // SAMPLE: FindJsonOrderedUsersByUsername
        [Fact]
        public void a_sorted_list_compiled_query_AsJson()
        {
            var user = theSession.Query(new FindJsonOrderedUsersByUsername() { FirstName = "Jeremy" });

            SpecificationExtensions.ShouldNotBeNull(user);
            user.ShouldBe($"[{_user1.ToJson()},{_user5.ToJson()}]");
        }

        // ENDSAMPLE

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
            var user = await theSession.QueryAsync(new FindJsonUsersByUsername() { FirstName = "Jeremy" }).ConfigureAwait(false);

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
            UserByUsername.Count = 0;

            var user = await theSession.QueryAsync(new UserByUsername { UserName = "myusername" }).ConfigureAwait(false);
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
            UsersByFirstNameWithFields.Count = 0;

            var users = theSession.Query(new UsersByFirstNameWithFields { FirstName = "Jeremy" }).ToList();
            users.Count.ShouldBe(2);
            users.ElementAt(0).UserName.ShouldBe("jdm");
            users.ElementAt(1).UserName.ShouldBe("shadetreedev");
            var differentUsers = theSession.Query(new UsersByFirstNameWithFields { FirstName = "Jeremy" });
            differentUsers.Count().ShouldBe(2);
            UsersByFirstNameWithFields.Count.ShouldBe(1);
        }

        [Fact]
        public async Task a_list_query_compiled_async()
        {
            var users = await theSession.QueryAsync(new UsersByFirstName { FirstName = "Jeremy" }).ConfigureAwait(false);
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
    }

    // SAMPLE: FindUserByAllTheThings
    public class FindUserByAllTheThings: ICompiledQuery<User>
    {
        public string Username { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }

        public Expression<Func<IQueryable<User>, User>> QueryIs()
        {
            return query =>
                    query.Where(x => x.FirstName == FirstName && Username == x.UserName)
                        .Where(x => x.LastName == LastName)
                        .Single();
        }
    }

    // ENDSAMPLE

    // SAMPLE: CompiledAsJson
    public class FindJsonUserByUsername: ICompiledQuery<User, string>
    {
        public string Username { get; set; }

        public Expression<Func<IQueryable<User>, string>> QueryIs()
        {
            return query =>
                    query.Where(x => Username == x.UserName)
                        .AsJson().Single();
        }
    }

    // ENDSAMPLE

    // SAMPLE: CompiledToJsonArray
    public class FindJsonOrderedUsersByUsername: ICompiledQuery<User, string>
    {
        public string FirstName { get; set; }

        public Expression<Func<IQueryable<User>, string>> QueryIs()
        {
            return query =>
                    query.Where(x => FirstName == x.FirstName)
                        .OrderBy(x => x.UserName)
                        .ToJsonArray();
        }
    }

    // ENDSAMPLE

    public class FindJsonUsersByUsername: ICompiledQuery<User, string>
    {
        public string FirstName { get; set; }

        public Expression<Func<IQueryable<User>, string>> QueryIs()
        {
            return query =>
                    query.Where(x => FirstName == x.FirstName)
                        .ToJsonArray();
        }
    }

    public class UserProjectionToLoginPayload: ICompiledQuery<User, LoginPayload>
    {
        public string UserName { get; set; }

        public Expression<Func<IQueryable<User>, LoginPayload>> QueryIs()
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

        public Expression<Func<IQueryable<User>, int>> QueryIs()
        {
            return query => query.Count(x => x.FirstName == FirstName);
        }
    }

    public class UserByUsername: ICompiledQuery<User>
    {
        public static int Count;
        public string UserName { get; set; }

        public Expression<Func<IQueryable<User>, User>> QueryIs()
        {
            Count++;
            return query => query.Where(x => x.UserName == UserName)
                .FirstOrDefault();
        }
    }

    public class UserByUsernameWithFields: ICompiledQuery<User>
    {
        public static int Count;
        public string UserName;

        public Expression<Func<IQueryable<User>, User>> QueryIs()
        {
            Count++;
            return query => query.Where(x => x.UserName == UserName)
                .FirstOrDefault();
        }
    }

    public class UserByUsernameSingleOrDefault: ICompiledQuery<User>
    {
        public static int Count;
        public string UserName { get; set; }

        public Expression<Func<IQueryable<User>, User>> QueryIs()
        {
            Count++;
            return query => query.Where(x => x.UserName == UserName)
                .SingleOrDefault();
        }
    }

    // SAMPLE: UsersByFirstName-Query
    public class UsersByFirstName: ICompiledListQuery<User>
    {
        public static int Count;
        public string FirstName { get; set; }

        public Expression<Func<IQueryable<User>, IEnumerable<User>>> QueryIs()
        {
            return query => query.Where(x => x.FirstName == FirstName);
        }
    }

    // ENDSAMPLE

    public class UsersByFirstNameWithFields: ICompiledListQuery<User>
    {
        public static int Count;
        public string FirstName;

        public Expression<Func<IQueryable<User>, IEnumerable<User>>> QueryIs()
        {
            // Ignore this line, it's from a unit test;)
            Count++;
            return query => query.Where(x => x.FirstName == FirstName);
        }
    }

    // SAMPLE: UserNamesForFirstName
    public class UserNamesForFirstName: ICompiledListQuery<User, string>
    {
        public Expression<Func<IQueryable<User>, IEnumerable<string>>> QueryIs()
        {
            return q => q
                .Where(x => x.FirstName == FirstName)
                .Select(x => x.UserName);
        }

        public string FirstName { get; set; }
    }

    // ENDSAMPLE
}
