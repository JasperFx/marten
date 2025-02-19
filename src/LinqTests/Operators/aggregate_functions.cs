using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;

namespace LinqTests.Operators;

public class aggregate_functions : IntegrationContext
{
    #region sample_using_max
    [Fact]
    public async Task get_max()
    {
        theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        theSession.Store(new Target { Color = Colors.Red, Number = 42 });
        theSession.Store(new Target { Color = Colors.Green, Number = 3 });
        theSession.Store(new Target { Color = Colors.Blue, Number = 4 });

        await theSession.SaveChangesAsync();
        var maxNumber = theSession.Query<Target>().Max(t => t.Number);
        maxNumber.ShouldBe(42);
    }
    #endregion

    [Fact]
    public async Task get_max_async()
    {
        theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        theSession.Store(new Target { Color = Colors.Red, Number = 42 });
        theSession.Store(new Target { Color = Colors.Green, Number = 3 });
        theSession.Store(new Target { Color = Colors.Blue, Number = 4 });

        await theSession.SaveChangesAsync();
        var maxNumber = await theSession.Query<Target>().MaxAsync(t => t.Number);
        maxNumber.ShouldBe(42);
    }

    #region sample_using_min
    [Fact]
    public async Task get_min()
    {
        theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        theSession.Store(new Target { Color = Colors.Red, Number = 2 });
        theSession.Store(new Target { Color = Colors.Green, Number = -5 });
        theSession.Store(new Target { Color = Colors.Blue, Number = 42 });

        await theSession.SaveChangesAsync();
        var minNumber = theSession.Query<Target>().Min(t => t.Number);
        minNumber.ShouldBe(-5);
    }
    #endregion

    [Fact]
    public async Task get_min_async()
    {
        theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        theSession.Store(new Target { Color = Colors.Red, Number = 42 });
        theSession.Store(new Target { Color = Colors.Green, Number = -5 });
        theSession.Store(new Target { Color = Colors.Blue, Number = 4 });

        await theSession.SaveChangesAsync();
        var maxNumber = await theSession.Query<Target>().MinAsync(t => t.Number);
        maxNumber.ShouldBe(-5);
    }

    #region sample_using_average
    [Fact]
    public async Task get_average()
    {
        theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        theSession.Store(new Target { Color = Colors.Red, Number = 2 });
        theSession.Store(new Target { Color = Colors.Green, Number = -5 });
        theSession.Store(new Target { Color = Colors.Blue, Number = 42 });

        await theSession.SaveChangesAsync();
        var average = theSession.Query<Target>().Average(t => t.Number);
        average.ShouldBe(10);
    }
    #endregion

    [Fact]
    public async Task get_average_async()
    {
        theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        theSession.Store(new Target { Color = Colors.Red, Number = 42 });
        theSession.Store(new Target { Color = Colors.Green, Number = -5 });
        theSession.Store(new Target { Color = Colors.Blue, Number = 2 });

        await theSession.SaveChangesAsync();
        var maxNumber = await theSession.Query<Target>().AverageAsync(t => t.Number);
        maxNumber.ShouldBe(10);
    }

    [Fact]
    public async Task sum_without_any_where()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        var result = theSession.Query<Target>().Sum(x => x.Number);
        result.ShouldBe(10);
    }

    [Fact]
    public async Task sum_with_nullable()
    {
        theSession.Store(new Target { NullableNumber = 1 });
        theSession.Store(new Target { NullableNumber = 2 });
        theSession.Store(new Target { NullableNumber = 3 });
        theSession.Store(new Target { NullableNumber = 4 });
        await theSession.SaveChangesAsync();

        var result = theSession.Query<Target>().Sum(x => x.NullableNumber);
        result.ShouldBe(10);
    }


    [Fact]
    public async Task sum_without_any_where_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        var result = await theSession.Query<Target>().SumAsync(x => x.Number);
        result.ShouldBe(10);
    }

    [Fact]
    public async Task sum_with_nullable_async()
    {
        theSession.Store(new Target { NullableNumber = 1 });
        theSession.Store(new Target { NullableNumber = 2 });
        theSession.Store(new Target { NullableNumber = 3 });
        theSession.Store(new Target { NullableNumber = 4 });
        await theSession.SaveChangesAsync();

        var result = await theSession.Query<Target>().SumAsync(x => x.NullableNumber);
        result.ShouldBe(10);
    }

        #region sample_using_sum
    [Fact]
    public async Task get_sum_of_integers()
    {
        theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        theSession.Store(new Target { Color = Colors.Red, Number = 2 });
        theSession.Store(new Target { Color = Colors.Green, Number = 3 });
        theSession.Store(new Target { Color = Colors.Blue, Number = 4 });

        await theSession.SaveChangesAsync();
        theSession.Query<Target>().Sum(x => x.Number)
            .ShouldBe(10);
    }

    #endregion

    [Fact]
    public async Task get_sum_of_decimals()
    {
        theSession.Store(new Target { Color = Colors.Blue, Decimal = 1.1m });
        theSession.Store(new Target { Color = Colors.Red, Decimal = 2.2m });
        theSession.Store(new Target { Color = Colors.Green, Decimal = 3.3m });

        await theSession.SaveChangesAsync();
        theSession.Query<Target>().Sum(x => x.Decimal)
            .ShouldBe(6.6m);
    }

    [Fact]
    public void get_sum_of_empty_table()
    {
        theSession.Query<Target>().Sum(x => x.Number)
            .ShouldBe(0);
    }

    [Fact]
    public async Task get_sum_of_integers_with_where()
    {
        theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        theSession.Store(new Target { Color = Colors.Red, Number = 2 });
        theSession.Store(new Target { Color = Colors.Green, Number = 3 });
        theSession.Store(new Target { Color = Colors.Blue, Number = 4 });

        await theSession.SaveChangesAsync();
        theSession.Query<Target>().Where(x => x.Number < 4).Sum(x => x.Number)
            .ShouldBe(6);
    }

    [Fact]
    public async Task get_sum_of_integers_with_where_async()
    {
        theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        theSession.Store(new Target { Color = Colors.Red, Number = 2 });
        theSession.Store(new Target { Color = Colors.Green, Number = 3 });
        theSession.Store(new Target { Color = Colors.Blue, Number = 4 });

        await theSession.SaveChangesAsync();
        (await theSession.Query<Target>().Where(x => x.Number < 4).SumAsync(x => x.Number))
            .ShouldBe(6);
    }

    [Theory]
    [InlineData(EnumStorage.AsString)]
    [InlineData(EnumStorage.AsInteger)]
    public async Task get_sum_of_integers_with_where_with_nullable_enum(EnumStorage enumStorage)
    {
        StoreOptions(o => o.UseDefaultSerialization(enumStorage));
        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();

        theSession.Store(new Target { NullableColor = Colors.Blue, Number = 1 });
        theSession.Store(new Target { NullableColor = Colors.Red, Number = 2 });
        theSession.Store(new Target { NullableColor = Colors.Green, Number = 3 });
        theSession.Store(new Target { NullableColor = null, Number = 4 });

        await theSession.SaveChangesAsync();
        theSession.Query<Target>()
            .Where(x => x.NullableColor != null)
            .Sum(x => x.Number)
            .ShouldBe(6);
    }


    public aggregate_functions(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
