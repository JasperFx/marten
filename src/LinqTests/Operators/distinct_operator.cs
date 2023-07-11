using System.Linq;
using Marten.Services.Json;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Operators;

public class distinct_operator : IntegrationContext
{
    private readonly ITestOutputHelper _output;

    [Fact]
    public void get_distinct_number()
    {
        theSession.Store(new Target {Number = 1});
        theSession.Store(new Target {Number = 1});
        theSession.Store(new Target {Number = 2});
        theSession.Store(new Target {Number = 2});
        theSession.Store(new Target {Number = 3});
        theSession.Store(new Target {Number = 3});

        theSession.SaveChanges();

        theSession.Logger = new TestOutputMartenLogger(_output);
        var queryable = theSession.Query<Target>().Select(x => x.Number).Distinct();

        queryable.ToList().Count.ShouldBe(3);
    }

    #region sample_get_distinct_numbers
    [SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
    public void get_distinct_numbers()
    {
        theSession.Store(new Target {Number = 1, Decimal = 1.0M});
        theSession.Store(new Target {Number = 1, Decimal = 2.0M});
        theSession.Store(new Target {Number = 1, Decimal = 2.0M});
        theSession.Store(new Target {Number = 2, Decimal = 1.0M});
        theSession.Store(new Target {Number = 2, Decimal = 2.0M});
        theSession.Store(new Target {Number = 2, Decimal = 1.0M});

        theSession.SaveChanges();

        var queryable = theSession.Query<Target>().Select(x => new
        {
            x.Number,
            x.Decimal
        }).Distinct();

        queryable.ToList().Count.ShouldBe(4);
    }
    #endregion

    #region sample_get_distinct_strings
    [Fact]
    public void get_distinct_string()
    {
        theSession.Store(new Target {String = "one"});
        theSession.Store(new Target {String = "one"});
        theSession.Store(new Target {String = "two"});
        theSession.Store(new Target {String = "two"});
        theSession.Store(new Target {String = "three"});
        theSession.Store(new Target {String = "three"});

        theSession.SaveChanges();

        var queryable = theSession.Query<Target>().Select(x => x.String).Distinct();

        queryable.ToList().Count.ShouldBe(3);
    }

    #endregion

    [Fact]
    public void get_distinct_strings()
    {
        theSession.Store(new Target {String = "one", AnotherString = "one"});
        theSession.Store(new Target {String = "one", AnotherString = "two"});
        theSession.Store(new Target {String = "one", AnotherString = "two"});
        theSession.Store(new Target {String = "two", AnotherString = "one"});
        theSession.Store(new Target {String = "two", AnotherString = "two"});
        theSession.Store(new Target {String = "two", AnotherString = "two"});

        theSession.SaveChanges();

        theSession.Logger = new TestOutputMartenLogger(_output);
        var queryable = theSession.Query<Target>().Select(x => new
        {
            x.String,
            x.AnotherString
        }).Distinct();

        queryable.ToList().Count.ShouldBe(4);
    }

    public distinct_operator(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }
}
