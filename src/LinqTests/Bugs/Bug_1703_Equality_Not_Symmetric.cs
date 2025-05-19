using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public sealed class Bug_1703_Equality_Not_Symmetric: IntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_1703_Equality_Not_Symmetric(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
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

            session.Query<Target>()
                .Where(x => x.String == (theString))
                .ToList()
                .Count
                .ShouldBe(1);

            session.Query<Target>()
                .Where(x => theString == x.String )
                .ToList()
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
            session.Query<Target>()
                .Where(x => x.String.Equals(theString))
                .ToList()
                .Count
                .ShouldBe(1);

            session.Query<Target>()
                .Where(x => theString.Equals(x.String))
                .ToList()
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

            session.Query<Target>()
                .Where(x => x.String.Equals(theString, StringComparison.InvariantCultureIgnoreCase))
                .ToList()
                .Count
                .ShouldBe(1);

            session.Query<Target>()
                .Where(x => theString.Equals(x.String, StringComparison.InvariantCultureIgnoreCase))
                .ToList()
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
            session.Logger = new TestOutputMartenLogger(_output);

            session.Query<Target>()
                .Where(x => x.Number.Equals(theNumber))
                .ToList()
                .Count
                .ShouldBe(1);

            session.Query<Target>()
                .Where(x => theNumber.Equals(x.Number))
                .ToList()
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
            session.Query<Target>()
                .Where(x => x.Number == theNumber )
                .ToList()
                .Count
                .ShouldBe(1);

            session.Query<Target>()
                .Where(x => theNumber == x.Number)
                .ToList()
                .Count
                .ShouldBe(1);
        }
    }
}
