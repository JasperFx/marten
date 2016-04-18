using System.Linq;
using Marten.Services;
using Marten.Testing.Fixtures;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class query_for_sum_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        // SAMPLE: using_sum
        [Fact]
        public void get_sum_of_integers()
        {
            theSession.Store(new Target{Color = Colors.Blue, Number = 1});
            theSession.Store(new Target{Color = Colors.Red, Number = 2});
            theSession.Store(new Target{Color = Colors.Green, Number = 3});
            theSession.Store(new Target{Color = Colors.Blue, Number = 4});

            theSession.SaveChanges();
            theSession.Query<Target>().Sum(x => x.Number)
                .ShouldBe(10);
        }
        // ENDSAMPLE

        [Fact]
        public void get_sum_of_decimals()
        {
            theSession.Store(new Target { Color = Colors.Blue, Decimal = 1.1m });
            theSession.Store(new Target { Color = Colors.Red, Decimal = 2.2m });
            theSession.Store(new Target { Color = Colors.Green, Decimal = 3.3m });

            theSession.SaveChanges();
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
        public void get_sum_of_integers_with_where()
        {
            theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
            theSession.Store(new Target { Color = Colors.Red, Number = 2 });
            theSession.Store(new Target { Color = Colors.Green, Number = 3 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 4 });

            theSession.SaveChanges();
            theSession.Query<Target>().Where(x => x.Number < 4).Sum(x => x.Number)
                .ShouldBe(6);
        }
    }
}