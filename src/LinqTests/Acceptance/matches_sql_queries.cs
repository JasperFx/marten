using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Linq.MatchesSql;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.SqlGeneration;
using Marten;

namespace LinqTests.Acceptance;

public class matches_sql_queries: IntegrationContext
{
    [Fact]
    public async Task query_using_matches_sql()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        // no where clause
        (await session.Query<User>().Where(x => x.MatchesSql("d.data ->> 'UserName' = ? or d.data ->> 'UserName' = ?", "baz", "jack")).OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("baz", "jack");

        // with a where clause
        (await session.Query<User>().Where(x => x.UserName != "baz" && x.MatchesSql("d.data ->> 'UserName' != ? and d.data ->> 'UserName' != ?", "foo", "bar"))
            .OrderBy(x => x.UserName)
            .ToListAsync())
            .Select(x => x.UserName)
            .Single().ShouldBe("jack");
    }

    [Fact]
    public async Task query_using_matches_json_path_with_parameters()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3);
        await session.SaveChangesAsync();

        // The JSONPath itself travels as a bound parameter rather than being concatenated into the
        // SQL, which is the reason to reach for the overload that takes them.
        (await session.Query<User>()
                .Where(x => x.MatchesJsonPath("d.data @? ^::jsonpath", "$ ? (@.UserName == \"baz\")"))
                .Select(x => x.UserName)
                .ToListAsync())
            .ShouldHaveTheSameElementsAs("baz");

        // More than one, to prove the parameters are not transposed or reused.
        (await session.Query<User>()
                .Where(x => x.MatchesJsonPath("d.data @? ^::jsonpath or d.data @? ^::jsonpath",
                    "$ ? (@.UserName == \"baz\")", "$ ? (@.UserName == \"foo\")"))
                .OrderBy(x => x.UserName)
                .Select(x => x.UserName)
                .ToListAsync())
            .ShouldHaveTheSameElementsAs("baz", "foo");
    }

    /// <summary>
    /// #5289 follow-up. The overload takes <c>params object[]</c>, so a caller may reasonably pass
    /// anything; the fix that made it usable at all only covered strings, because
    /// <c>AppendWithParameters</c> seeds each placeholder with the provider's STRING parameter type and
    /// assigning <c>.Value</c> alone leaves it there. A number therefore still failed with the same
    /// exception the fix was for.
    /// </summary>
    [Fact]
    public async Task query_using_matches_json_path_with_a_non_string_parameter()
    {
        var user1 = new User { UserName = "foo", Age = 30 };
        var user2 = new User { UserName = "bar", Age = 60 };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2);
        await session.SaveChangesAsync();

        // The value bound on its own, into a JSONPath variable rather than into the path text -- the
        // shape a caller-supplied filter value has to take if it is not to be concatenated in.
        (await session.Query<User>()
                .Where(x => x.MatchesJsonPath(
                    "jsonb_path_exists(d.data, '$ ? (@.Age <= $v)', jsonb_build_object('v', ^))", 30))
                .Select(x => x.UserName)
                .ToListAsync())
            .ShouldHaveTheSameElementsAs("foo");
    }

    /// <summary>
    /// #5289 follow-up. <c>CommandParameter</c> maps null onto <c>DBNull.Value</c>, which is why
    /// <c>MatchesSql</c> accepts a null argument. Assigning the raw value overwrote the
    /// <c>DBNull.Value</c> that <c>AppendWithParameters</c> had already put there, and Npgsql refuses a
    /// CLR null outright.
    /// </summary>
    [Fact]
    public async Task query_using_matches_json_path_with_a_null_parameter()
    {
        using var session = theStore.LightweightSession();
        session.Store(new User { UserName = "foo" });
        await session.SaveChangesAsync();

        // NULL::jsonpath matches nothing rather than throwing, which is the point: a null filter value
        // should return no rows, not take the query down.
        (await session.Query<User>()
                .Where(x => x.MatchesJsonPath("d.data @? ^::jsonpath", new object[] { null }))
                .ToListAsync())
            .ShouldBeEmpty();
    }

    /// <summary>
    /// #5289 follow-up. '^' is the placeholder character for this overload and is also a regex anchor,
    /// so it turns up inside JSONPath literals by accident — <c>like_regex "^ba"</c> is one stray
    /// placeholder. That used to surface as an IndexOutOfRangeException thrown from inside the LINQ
    /// provider, and in the opposite direction (more values than placeholders) as silence, with the
    /// surplus values never reaching the query at all.
    /// </summary>
    [Fact]
    public async Task mismatched_json_path_placeholder_count_is_reported()
    {
        using var session = theStore.LightweightSession();

        var tooFew = await Should.ThrowAsync<BadLinqExpressionException>(() => session.Query<User>()
            .Where(x => x.MatchesJsonPath("d.data @? '$ ? (@.UserName like_regex \"^ba\")'"))
            .ToListAsync());

        tooFew.Message.ShouldContain("0 parameter(s)");
        tooFew.Message.ShouldContain("1 '^' placeholder(s)");

        await Should.ThrowAsync<BadLinqExpressionException>(() => session.Query<User>()
            .Where(x => x.MatchesJsonPath("d.data @? ^::jsonpath", "$ ? (@.UserName == \"baz\")", "spare"))
            .ToListAsync());
    }

    [Fact]
    public async Task query_using_where_fragment()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        var whereFragment = CompoundWhereFragment.And();
        whereFragment.Add(new WhereFragment("d.data ->> 'UserName' != ?", "baz"));
        whereFragment.Add(new WhereFragment("d.data ->> 'UserName' != ?", "jack"));

        // no where clause
        (await session.Query<User>().Where(x => x.MatchesSql(whereFragment)).OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToListAsync()).ShouldHaveTheSameElementsAs("bar", "foo");

        // with a where clause
        (await session.Query<User>().Where(x => x.UserName != "bar" && x.MatchesSql(whereFragment))
            .OrderBy(x => x.UserName)
            .ToListAsync())
            .Select(x => x.UserName)
            .Single().ShouldBe("foo");
    }

    [Fact]
    public void Throws_NotSupportedException_when_called_directly()
    {
        Should.Throw<NotSupportedException>(
            () => new object().MatchesSql("d.data ->> 'UserName' = ? or d.data ->> 'UserName' = ?", "baz", "jack"));
        Should.Throw<NotSupportedException>(
            () => new object().MatchesSql(new WhereFragment("d.data ->> 'UserName' != ?", "baz")));
    }

    protected override async Task fixtureSetup()
    {
        await theStore.Advanced.ResetAllData();
    }

    public matches_sql_queries(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
