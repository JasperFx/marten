using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Acceptance;

public class number_filtering : IntegrationContext
{
    private readonly ITestOutputHelper _output;

    public number_filtering(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }

    [Fact]
    public async Task can_query_by_float()
    {
        var target1 = new Target {Float = 123.45F};
        var target2 = new Target {Float = 456.45F};

        theSession.Store(target1, target2);
        theSession.Store(Target.GenerateRandomData(5).ToArray());

        await theSession.SaveChangesAsync();

        (await theSession.Query<Target>().Where(x => x.Float > 400).ToListAsync()).Select(x => x.Id)
            .ShouldContain(x => x == target2.Id);
    }

    private static readonly BigInteger SmallNumber = BigInteger.Parse("1000000");
    private static readonly BigInteger MediumNumber = BigInteger.Parse(long.MaxValue.ToString());
    private static readonly BigInteger LargeNumber = BigInteger.Parse("123456789012345678901234567890123456789012345678901234567890");

    [Fact]
    public async Task can_query_by_BigInteger()
    {
        StoreOptions(options =>
        {
            options.Schema.For<BigIntegerObject>();
        });

        using (var session = theStore.LightweightSession())
        {
            session.Store(new BigIntegerObject
            {
                Id = 1,
                Value = SmallNumber
            });
            session.Store(new BigIntegerObject
            {
                Id = 2,
                Value = MediumNumber
            });
            session.Store(new BigIntegerObject
            {
                Id = 3,
                Value = LargeNumber
            });
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Logger = new TestOutputMartenLogger(_output);
            var findSmallerThanMedium = await session.Query<BigIntegerObject>().SingleAsync(x => x.Value < MediumNumber);

            findSmallerThanMedium.Id.ShouldBe(1);

            var findLargeNumberExact = await session.Query<BigIntegerObject>().SingleAsync(x => x.Value == LargeNumber);

            findLargeNumberExact.Id.ShouldBe(3);

            var allNumbersLargerThanSmall = await session.Query<BigIntegerObject>().Where(x => x.Value > SmallNumber).ToListAsync();

            allNumbersLargerThanSmall.ShouldContain(x => x.Id == 2);
            allNumbersLargerThanSmall.ShouldContain(x => x.Id == 3);
        }
    }
}


public class BigIntegerObject
{
    public int Id { get; set; }
    public BigInteger Value { get; set; }
}
