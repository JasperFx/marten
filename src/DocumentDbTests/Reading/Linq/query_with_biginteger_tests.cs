using System.Linq;
using System.Numerics;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading.Linq;

public class query_with_biginteger_tests : IntegrationContext
{
    private static readonly BigInteger SmallNumber = BigInteger.Parse("1000000");
    private static readonly BigInteger MediumNumber = BigInteger.Parse(long.MaxValue.ToString());
    private static readonly BigInteger LargeNumber = BigInteger.Parse("123456789012345678901234567890123456789012345678901234567890");

    [Fact]
    public void Basic_Queries()
    {
        StoreOptions(options =>
        {
            options.Schema.For<BigIntegerObject>();
        });

        using (var session = theStore.OpenSession())
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
            session.SaveChanges();
        }

        using (var session = theStore.LightweightSession())
        {
            var findSmallerThanMedium = session.Query<BigIntegerObject>().Single(x => x.Value < MediumNumber);

            findSmallerThanMedium.Id.ShouldBe(1);

            var findLargeNumberExact = session.Query<BigIntegerObject>().Single(x => x.Value == LargeNumber);

            findLargeNumberExact.Id.ShouldBe(3);

            var allNumbersLargerThanSmall = session.Query<BigIntegerObject>().Where(x => x.Value > SmallNumber).ToList();

            allNumbersLargerThanSmall.ShouldContain(x => x.Id == 2);
            allNumbersLargerThanSmall.ShouldContain(x => x.Id == 3);
        }
    }

    public query_with_biginteger_tests(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}

public class BigIntegerObject
{
    public int Id { get; set; }
    public BigInteger Value { get; set; }
}