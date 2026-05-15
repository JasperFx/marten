using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Marten;
namespace LinqTests.Bugs;

public sealed class Bug_1703_Equality_Not_Symmetric: IntegrationContext
{
    public Bug_1703_Equality_Not_Symmetric(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    protected override async Task fixtureSetup()
    {
        await theStore.Advanced.ResetAllData();
    }

    [Fact]
    public async Task string_equality_equals_operator_should_be_symmetric()
    {
        var random = Target.Random();
        var theString = random.String;
        using (var session = theStore.LightweightSession())
        {
            session.Insert(random);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {

            (await session.Query<Target>()
                .Where(x => x.String == (theString))
                .ToListAsync())
                .Count
                .ShouldBe(1);

            (await session.Query<Target>()
                .Where(x => theString == x.String )
                .ToListAsync())
                .Count
                .ShouldBe(1);
        }
    }

    [Fact]
    public async Task string_equality_equals_should_be_symmetric()
    {
        var random = Target.Random();
        var theString = random.String;
        await using (var session = theStore.LightweightSession())
        {
            session.Insert(random);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.QuerySession())
        {
            (await session.Query<Target>()
                .Where(x => x.String.Equals(theString))
                .ToListAsync())
                .Count
                .ShouldBe(1);

            (await session.Query<Target>()
                .Where(x => theString.Equals(x.String))
                .ToListAsync())
                .Count
                .ShouldBe(1);
        }
    }



    [Fact]
    public async Task string_equality_equals_ignoring_case_should_be_symmetric()
    {
        var random = Target.Random();
        var theString = random.String;
        await using (var session = theStore.LightweightSession())
        {
            session.Insert(random);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.QuerySession())
        {

            (await session.Query<Target>()
                .Where(x => x.String.Equals(theString, StringComparison.InvariantCultureIgnoreCase))
                .ToListAsync())
                .Count
                .ShouldBe(1);

            (await session.Query<Target>()
                .Where(x => theString.Equals(x.String, StringComparison.InvariantCultureIgnoreCase))
                .ToListAsync())
                .Count
                .ShouldBe(1);
        }
    }

    [Fact]
    public async Task object_equality_equals_should_be_symmetric()
    {
        var random = Target.Random();
        var theNumber = random.Number;
        await using (var session = theStore.LightweightSession())
        {
            session.Insert(random);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.QuerySession())
        {
            (await session.Query<Target>()
                .Where(x => x.Number.Equals(theNumber))
                .ToListAsync())
                .Count
                .ShouldBe(1);

            (await session.Query<Target>()
                .Where(x => theNumber.Equals(x.Number))
                .ToListAsync())
                .Count
                .ShouldBe(1);
        }
    }

    [Fact]
    public async Task object_equality_equals_operator_should_be_symmetric()
    {
        var random = Target.Random();
        var theNumber = random.Number;
        await using (var session = theStore.LightweightSession())
        {
            session.Insert(random);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.QuerySession())
        {
            (await session.Query<Target>()
                .Where(x => x.Number == theNumber )
                .ToListAsync())
                .Count
                .ShouldBe(1);

            (await session.Query<Target>()
                .Where(x => theNumber == x.Number)
                .ToListAsync())
                .Count
                .ShouldBe(1);
        }
    }
}
