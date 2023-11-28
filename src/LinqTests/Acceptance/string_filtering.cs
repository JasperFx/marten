using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Acceptance;

public class string_filtering: IntegrationContext
{
    protected override Task fixtureSetup()
    {
        var entry = new User { FirstName = "Beeblebrox", NickName = "Beebl" };
        var entry2 = new User { FirstName = "Bee", NickName = "Bee" };
        var entry3 = new User { FirstName = "Zaphod", NickName = "" };
        var entry4 = new User { FirstName = "Zap", NickName = null };

        return theStore.BulkInsertAsync(new[] { entry, entry2, entry3, entry4 });
    }

    [Theory]
    [InlineData("zap", StringComparison.OrdinalIgnoreCase, 1)]
    [InlineData("Zap", StringComparison.CurrentCulture, 1)]
    [InlineData("zap", StringComparison.CurrentCulture, 0)]
    public void CanQueryByEquals(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = s.Query<User>().Where(x => x.FirstName.Equals(search, comparison)).ToList();

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => x.FirstName.Equals(search, comparison)));
    }

    [Theory]
    [InlineData("zap", StringComparison.OrdinalIgnoreCase, 3)]
    [InlineData("Zap", StringComparison.CurrentCulture, 3)]
    [InlineData("zap", StringComparison.CurrentCulture, 4)]
    public void CanQueryByNotEquals(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = s.Query<User>().Where(x => !x.FirstName.Equals(search, comparison)).ToList();

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => !x.FirstName.Equals(search, comparison)));
    }

    [Theory]
    [InlineData("zap", StringComparison.OrdinalIgnoreCase, 2)]
    [InlineData("zap", StringComparison.CurrentCulture, 0)]
    public void CanQueryByContains(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = s.Query<User>().Where(x => x.FirstName.Contains(search, comparison)).ToList();

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => x.FirstName.Contains(search, comparison)));
    }

    [Theory]
    [InlineData("zap", StringComparison.OrdinalIgnoreCase, 2)]
    [InlineData("zap", StringComparison.CurrentCulture, 4)]
    public void CanQueryByNotContains(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = s.Query<User>().Where(x => !x.FirstName.Contains(search, comparison)).ToList();

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => !x.FirstName.Contains(search, comparison)));
    }

    [Theory]
    [InlineData("zap", StringComparison.OrdinalIgnoreCase, 2)]
    [InlineData("zap", StringComparison.CurrentCulture, 0)]
    public void CanQueryByStartsWith(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = s.Query<User>().Where(x => x.FirstName.StartsWith(search, comparison)).ToList();

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => x.FirstName.StartsWith(search, comparison)));
    }

    [Theory]
    [InlineData("zap", StringComparison.OrdinalIgnoreCase, 2)]
    [InlineData("zap", StringComparison.CurrentCulture, 4)]
    public void CanQueryByNotStartsWith(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = s.Query<User>().Where(x => !x.FirstName.StartsWith(search, comparison)).ToList();

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => !x.FirstName.StartsWith(search, comparison)));
    }

    [Theory]
    [InlineData("hod", StringComparison.OrdinalIgnoreCase, 1)]
    [InlineData("HOD", StringComparison.OrdinalIgnoreCase, 1)]
    [InlineData("Hod", StringComparison.CurrentCulture, 0)]
    public void CanQueryByEndsWith(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = s.Query<User>().Where(x => x.FirstName.EndsWith(search, comparison)).ToList();

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => x.FirstName.EndsWith(search, comparison)));
    }

    [Theory]
    [InlineData("hod", StringComparison.OrdinalIgnoreCase, 3)]
    [InlineData("HOD", StringComparison.OrdinalIgnoreCase, 3)]
    [InlineData("Hod", StringComparison.CurrentCulture, 4)]
    public void CanQueryByNotEndsWith(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = s.Query<User>().Where(x => !x.FirstName.EndsWith(search, comparison)).ToList();

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => !x.FirstName.EndsWith(search, comparison)));
    }

    [Fact]
    public void CanQueryByIsNullOrEmpty()
    {
        using var s = theStore.QuerySession();
        var fromDb = s.Query<User>().Where(x => string.IsNullOrEmpty(x.NickName)).ToList();

        Assert.Equal(2, fromDb.Count);
        Assert.True(fromDb.All(x => string.IsNullOrEmpty(x.NickName)));
    }

    [Fact]
    public void CanQueryByNotIsNullOrEmpty()
    {
        using var s = theStore.QuerySession();
        var fromDb = s.Query<User>().Where(x => !string.IsNullOrEmpty(x.NickName)).ToList();

        Assert.Equal(2, fromDb.Count);
        Assert.True(fromDb.All(x => !string.IsNullOrEmpty(x.NickName)));
    }

    [Theory]
    [InlineData("zap", "hod", StringComparison.OrdinalIgnoreCase, 1)]
    [InlineData("zap", "hod", StringComparison.CurrentCulture, 0)]
    public void CanMixContainsAndNotContains(string contains, string notContains, StringComparison comparison,
        int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = s.Query<User>().Where(x =>
            !x.FirstName.Contains(notContains, comparison) && x.FirstName.Contains(contains, comparison)).ToList();
        Assert.Equal(expectedCount, fromDb.Count);
    }

    [Fact]
    public void can_search_case_insensitive()
    {
        var user1 = new User { UserName = "Abc" };
        var user2 = new User { UserName = "DeF" };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user1, user2);
            session.SaveChanges();
        }

        using (var query = theStore.QuerySession())
        {
            #region sample_sample-linq-EqualsIgnoreCase

            query.Query<User>().Single(x => x.UserName.EqualsIgnoreCase("abc")).Id.ShouldBe(user1.Id);
            query.Query<User>().Single(x => x.UserName.EqualsIgnoreCase("aBc")).Id.ShouldBe(user1.Id);

            #endregion

            query.Query<User>().Single(x => x.UserName.EqualsIgnoreCase("def")).Id.ShouldBe(user2.Id);

            query.Query<User>().Any(x => x.UserName.EqualsIgnoreCase("abcd")).ShouldBeFalse();
        }
    }

    [Fact]
    public void can_search_case_insensitive_with_StringComparison()
    {
        var user = new User { UserName = "TEST_USER" };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            session.SaveChanges();
        }

        using (var query = theStore.QuerySession())
        {
            query.Query<User>().Single(x => x.UserName.Equals("test_user", StringComparison.InvariantCultureIgnoreCase))
                .Id.ShouldBe(user.Id);
        }
    }

    [Fact]
    public void can_search_string_with_back_slash_case_insensitive_with_StringComparison()
    {
        var user = new User { UserName = @"DOMAIN\TEST_USER" };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            session.SaveChanges();
        }

        using (var query = theStore.QuerySession())
        {
            query.Query<User>()
                .Single(x => x.UserName.Equals(@"domain\test_user", StringComparison.InvariantCultureIgnoreCase)).Id
                .ShouldBe(user.Id);
        }
    }


    public string_filtering(DefaultStoreFixture fixture): base(fixture)
    {
    }
}
