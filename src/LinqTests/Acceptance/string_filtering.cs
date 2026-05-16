using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Marten;
namespace LinqTests.Acceptance;

public class string_filtering: IntegrationContext
{
    protected override async Task fixtureSetup()
    {
        await theStore.Advanced.ResetAllData();
        var entry = new User { FirstName = "Beeblebrox", Nickname = "" };
        var entry2 = new User { FirstName = "Bee", Nickname = "   " };
        var entry3 = new User { FirstName = "Zaphod", Nickname = "Zaph" };
        var entry4 = new User { FirstName = "Zap", Nickname = null };

        await theStore.BulkInsertAsync(new[] { entry, entry2, entry3, entry4 });
    }

    [Theory]
    [InlineData("zap", StringComparison.OrdinalIgnoreCase, 1)]
    [InlineData("Zap", StringComparison.CurrentCulture, 1)]
    [InlineData("zap", StringComparison.CurrentCulture, 0)]
    public async Task CanQueryByEquals(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = (await s.Query<User>().Where(x => x.FirstName.Equals(search, comparison)).ToListAsync());

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => x.FirstName.Equals(search, comparison)));
    }

    [Theory]
    [InlineData("zap", StringComparison.OrdinalIgnoreCase, 3)]
    [InlineData("Zap", StringComparison.CurrentCulture, 3)]
    [InlineData("zap", StringComparison.CurrentCulture, 4)]
    public async Task CanQueryByNotEquals(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = (await s.Query<User>().Where(x => !x.FirstName.Equals(search, comparison)).ToListAsync());

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => !x.FirstName.Equals(search, comparison)));
    }

    [Theory]
    [InlineData("zap", StringComparison.OrdinalIgnoreCase, 2)]
    [InlineData("zap", StringComparison.CurrentCulture, 0)]
    public async Task CanQueryByContains(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = (await s.Query<User>().Where(x => x.FirstName.Contains(search, comparison)).ToListAsync());

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => x.FirstName.Contains(search, comparison)));
    }

    [Theory]
    [InlineData("zap", StringComparison.OrdinalIgnoreCase, 2)]
    [InlineData("zap", StringComparison.CurrentCulture, 4)]
    public async Task CanQueryByNotContains(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = (await s.Query<User>().Where(x => !x.FirstName.Contains(search, comparison)).ToListAsync());

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => !x.FirstName.Contains(search, comparison)));
    }

    [Theory]
    [InlineData("zap", StringComparison.OrdinalIgnoreCase, 2)]
    [InlineData("zap", StringComparison.CurrentCulture, 0)]
    public async Task CanQueryByStartsWith(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = (await s.Query<User>().Where(x => x.FirstName.StartsWith(search, comparison)).ToListAsync());

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => x.FirstName.StartsWith(search, comparison)));
    }

    [Theory]
    [InlineData("zap", StringComparison.OrdinalIgnoreCase, 2)]
    [InlineData("zap", StringComparison.CurrentCulture, 4)]
    public async Task CanQueryByNotStartsWith(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = (await s.Query<User>().Where(x => !x.FirstName.StartsWith(search, comparison)).ToListAsync());

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => !x.FirstName.StartsWith(search, comparison)));
    }

    [Theory]
    [InlineData("hod", StringComparison.OrdinalIgnoreCase, 1)]
    [InlineData("HOD", StringComparison.OrdinalIgnoreCase, 1)]
    [InlineData("Hod", StringComparison.CurrentCulture, 0)]
    public async Task CanQueryByEndsWith(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = (await s.Query<User>().Where(x => x.FirstName.EndsWith(search, comparison)).ToListAsync());

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => x.FirstName.EndsWith(search, comparison)));
    }

    [Theory]
    [InlineData("hod", StringComparison.OrdinalIgnoreCase, 3)]
    [InlineData("HOD", StringComparison.OrdinalIgnoreCase, 3)]
    [InlineData("Hod", StringComparison.CurrentCulture, 4)]
    public async Task CanQueryByNotEndsWith(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = (await s.Query<User>().Where(x => !x.FirstName.EndsWith(search, comparison)).ToListAsync());

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x => !x.FirstName.EndsWith(search, comparison)));
    }

    [Fact]
    public async Task CanQueryByIsNullOrEmpty()
    {
        using var s = theStore.QuerySession();
        var fromDb = (await s.Query<User>().Where(x => string.IsNullOrEmpty(x.Nickname)).ToListAsync());

        Assert.Equal(2, fromDb.Count);
        Assert.True(fromDb.All(x => string.IsNullOrEmpty(x.Nickname)));
    }

    [Fact]
    public async Task CanQueryByNotIsNullOrEmpty()
    {
        using var s = theStore.QuerySession();
        var fromDb = (await s.Query<User>().Where(x => !string.IsNullOrEmpty(x.Nickname)).ToListAsync());

        Assert.Equal(2, fromDb.Count);
        Assert.True(fromDb.All(x => !string.IsNullOrEmpty(x.Nickname)));
    }

    [Fact]
    public async Task CanQueryByIsNullOrWhiteSpace()
    {
        using var s = theStore.QuerySession();
        var fromDb = (await s.Query<User>().Where(x => string.IsNullOrWhiteSpace(x.Nickname)).ToListAsync());

        Assert.Equal(3, fromDb.Count);
        Assert.True(fromDb.All(x => string.IsNullOrWhiteSpace(x.Nickname)));
    }

    [Fact]
    public async Task CanQueryByNotIsNullOrWhiteSpace()
    {
        using var s = theStore.QuerySession();
        var fromDb = (await s.Query<User>().Where(x => !string.IsNullOrWhiteSpace(x.Nickname)).ToListAsync());

        Assert.Single(fromDb);
        Assert.True(fromDb.All(x => !string.IsNullOrWhiteSpace(x.Nickname)));
    }

    [Theory]
    [InlineData("zap", "hod", StringComparison.OrdinalIgnoreCase, 1)]
    [InlineData("zap", "hod", StringComparison.CurrentCulture, 0)]
    public async Task CanMixContainsAndNotContains(string contains, string notContains, StringComparison comparison,
        int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = (await s.Query<User>().Where(x =>
            !x.FirstName.Contains(notContains, comparison) && x.FirstName.Contains(contains, comparison)).ToListAsync());

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(fromDb.All(x =>
            !x.FirstName.Contains(notContains, comparison) && x.FirstName.Contains(contains, comparison)));
    }

    [Theory]
    [InlineData("hod", StringComparison.OrdinalIgnoreCase, 1)]
    [InlineData("HOD", StringComparison.OrdinalIgnoreCase, 1)]
    [InlineData("Hod", StringComparison.CurrentCulture, 2)]
    public async Task CanMixNotEndsWithWithNotIsNullOrEmpty(string search, StringComparison comparison,
        int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = (await s.Query<User>()
            .Where(x => !x.FirstName.EndsWith(search, comparison) && !string.IsNullOrEmpty(x.Nickname)).ToListAsync());

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(
            fromDb.All(x => !x.FirstName.EndsWith(search, comparison) && !string.IsNullOrEmpty(x.Nickname)));
    }

    [Theory]
    [InlineData("zap", StringComparison.OrdinalIgnoreCase, 1)]
    [InlineData("zap", StringComparison.CurrentCulture, 0)]
    public async Task CanMixStartsWithAndIsNullOrWhiteSpace(string search, StringComparison comparison, int expectedCount)
    {
        using var s = theStore.QuerySession();
        var fromDb = (await s.Query<User>()
            .Where(x => x.FirstName.StartsWith(search, comparison) && string.IsNullOrWhiteSpace(x.Nickname)).ToListAsync());

        Assert.Equal(expectedCount, fromDb.Count);
        Assert.True(
            fromDb.All(x => x.FirstName.StartsWith(search, comparison) && string.IsNullOrWhiteSpace(x.Nickname)));
    }

    [Fact]
    public async Task can_search_case_insensitive()
    {
        var user1 = new User { UserName = "Abc" };
        var user2 = new User { UserName = "DeF" };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user1, user2);
            await session.SaveChangesAsync();
        }

        using (var query = theStore.QuerySession())
        {
            #region sample_sample-linq-equalsignorecase

            (await query.Query<User>().SingleAsync(x => x.UserName.EqualsIgnoreCase("abc"))).Id.ShouldBe(user1.Id);
            (await query.Query<User>().SingleAsync(x => x.UserName.EqualsIgnoreCase("aBc"))).Id.ShouldBe(user1.Id);

            #endregion

            (await query.Query<User>().SingleAsync(x => x.UserName.EqualsIgnoreCase("def"))).Id.ShouldBe(user2.Id);

            (await query.Query<User>().AnyAsync(x => x.UserName.EqualsIgnoreCase("abcd"))).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task can_search_case_insensitive_with_StringComparison()
    {
        var user = new User { UserName = "TEST_USER" };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            await session.SaveChangesAsync();
        }

        using (var query = theStore.QuerySession())
        {
            (await query.Query<User>().SingleAsync(x => x.UserName.Equals("test_user", StringComparison.InvariantCultureIgnoreCase)))
                .Id.ShouldBe(user.Id);
        }
    }

    [Fact]
    public async Task can_search_string_with_back_slash_case_insensitive_with_StringComparison()
    {
        var user = new User { UserName = @"DOMAIN\TEST_USER" };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            await session.SaveChangesAsync();
        }

        using (var query = theStore.QuerySession())
        {
            (await query.Query<User>()
                .SingleAsync(x => x.UserName.Equals(@"domain\test_user", StringComparison.InvariantCultureIgnoreCase))).Id
                .ShouldBe(user.Id);
        }
    }


    public string_filtering(DefaultStoreFixture fixture): base(fixture)
    {
    }
}
