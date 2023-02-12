using System;
using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading.Linq;

public class InvariantCultureIgnoreCase_filtering: IntegrationContext
{
    [Fact]
    public void can_search_case_insensitive()
    {
        var user = new User {UserName = "TEST_USER"};

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            session.SaveChanges();
        }

        using (var query = theStore.QuerySession())
        {
            query.Query<User>().Single(x => x.UserName.Equals("test_user", StringComparison.InvariantCultureIgnoreCase)).Id.ShouldBe(user.Id);
        }
    }

    [Fact]
    public void can_search_string_with_back_slash_case_insensitive()
    {
        var user = new User {UserName = @"DOMAIN\TEST_USER"};

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            session.SaveChanges();
        }

        using (var query = theStore.QuerySession())
        {
            query.Query<User>().Single(x => x.UserName.Equals(@"domain\test_user", StringComparison.InvariantCultureIgnoreCase)).Id.ShouldBe(user.Id);
        }
    }

    public InvariantCultureIgnoreCase_filtering(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
