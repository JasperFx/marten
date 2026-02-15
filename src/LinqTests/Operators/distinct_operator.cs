using System.Linq;
using System.Threading.Tasks;
using Marten.Services.Json;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
namespace LinqTests.Operators;

public class distinct_operator : IntegrationContext
{
    [Fact]
    public async Task get_distinct_number()
    {
        theSession.Store(new Target {Number = 1});
        theSession.Store(new Target {Number = 1});
        theSession.Store(new Target {Number = 2});
        theSession.Store(new Target {Number = 2});
        theSession.Store(new Target {Number = 3});
        theSession.Store(new Target {Number = 3});

        await theSession.SaveChangesAsync();


        var queryable = theSession.Query<Target>().Select(x => x.Number).Distinct();

        queryable.ToList().Count.ShouldBe(3);
    }

    #region sample_get_distinct_numbers
    [SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
    public async Task get_distinct_numbers()
    {
        theSession.Store(new Target {Number = 1, Decimal = 1.0M});
        theSession.Store(new Target {Number = 1, Decimal = 2.0M});
        theSession.Store(new Target {Number = 1, Decimal = 2.0M});
        theSession.Store(new Target {Number = 2, Decimal = 1.0M});
        theSession.Store(new Target {Number = 2, Decimal = 2.0M});
        theSession.Store(new Target {Number = 2, Decimal = 1.0M});

        await theSession.SaveChangesAsync();

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
    public async Task get_distinct_string()
    {
        theSession.Store(new Target {String = "one"});
        theSession.Store(new Target {String = "one"});
        theSession.Store(new Target {String = "two"});
        theSession.Store(new Target {String = "two"});
        theSession.Store(new Target {String = "three"});
        theSession.Store(new Target {String = "three"});

        await theSession.SaveChangesAsync();

        var queryable = theSession.Query<Target>().Select(x => x.String).Distinct();

        queryable.ToList().Count.ShouldBe(3);
    }

    #endregion

    [Fact]
    public async Task get_distinct_strings()
    {
        theSession.Store(new Target {String = "one", AnotherString = "one"});
        theSession.Store(new Target {String = "one", AnotherString = "two"});
        theSession.Store(new Target {String = "one", AnotherString = "two"});
        theSession.Store(new Target {String = "two", AnotherString = "one"});
        theSession.Store(new Target {String = "two", AnotherString = "two"});
        theSession.Store(new Target {String = "two", AnotherString = "two"});

        await theSession.SaveChangesAsync();


        var queryable = theSession.Query<Target>().Select(x => new
        {
            x.String,
            x.AnotherString
        }).Distinct();

        queryable.ToList().Count.ShouldBe(4);
    }

    [Fact]
    public async Task get_distinct_enums()
    {
        theSession.Store(new Target { Color = Colors.Green });
        theSession.Store(new Target { Color = Colors.Blue });
        theSession.Store(new Target { Color = Colors.Blue });
        theSession.Store(new Target { Color = Colors.Red });
        theSession.Store(new Target { Color = Colors.Blue });
        theSession.Store(new Target { Color = Colors.Yellow });

        await theSession.SaveChangesAsync();


        var queryable = theSession.Query<Target>().Select(x => x.Color).Distinct();

        queryable.ToList().Count.ShouldBe(4);
    }

    [Fact]
    public async Task get_distinct_nullable_enums()
    {
        theSession.Store(new Target { NullableEnum = Colors.Green });
        theSession.Store(new Target { NullableEnum = Colors.Blue });
        theSession.Store(new Target { NullableEnum = Colors.Blue });
        theSession.Store(new Target { NullableEnum = Colors.Red });
        theSession.Store(new Target { NullableEnum = Colors.Blue });
        theSession.Store(new Target { NullableEnum = Colors.Yellow });
        theSession.Store(new Target { NullableEnum = null });

        await theSession.SaveChangesAsync();


        var queryable = theSession.Query<Target>()
            .Select(x => x.NullableEnum).Distinct();

        queryable.ToList().Count.ShouldBe(5);
    }

    [Fact]
    public async Task get_distinct_nullable_string_enums()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(enumStorage: EnumStorage.AsString);
        });
        theSession.Store(new Target { NullableEnum = Colors.Green });
        theSession.Store(new Target { NullableEnum = Colors.Blue });
        theSession.Store(new Target { NullableEnum = Colors.Blue });
        theSession.Store(new Target { NullableEnum = Colors.Red });
        theSession.Store(new Target { NullableEnum = Colors.Blue });
        theSession.Store(new Target { NullableEnum = Colors.Yellow });
        theSession.Store(new Target { NullableEnum = null });

        await theSession.SaveChangesAsync();


        var queryable = theSession.Query<Target>()
            .Select(x => x.NullableEnum).Distinct();

        queryable.ToList().Count.ShouldBe(5);
    }

    public distinct_operator(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
