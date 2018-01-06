using System.Linq;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Linq
{
    [ControlledQueryStoryteller]
    public class query_beginning_with_equal_to_value_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void start_with_constant()
        {
            theSession.Store(new Target{Number = 1});
            theSession.Store(new Target{Number = 2});

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => 2 == x.Number).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(2);
        }

        [Fact]
        public void start_with_constant_2()
        {
            theSession.Store(new Target{Number = 1});
            theSession.Store(new Target{Number = 2});

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => 1 < x.Number).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(2);
        }

        [Fact]
        public void start_with_null_constant()
        {
            theSession.Store(new Target {Number = 1});
            theSession.Store(new Target{NullableNumber = 2, Number = 2});

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => null == x.NullableNumber).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1);
        }

        [Fact]
        public void later_expression_starts_with_constant()
        {
            theSession.Store(new Target{Number = 1});
            theSession.Store(new Target{Number = 2});

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Number == 1 || 2 == x.Number).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1,2);
        }

        [Fact]
        public void start_with_variable()
        {
            theSession.Store(new Target{Number = 1});
            theSession.Store(new Target{Number = 2});

            theSession.SaveChanges();
            var num = 2;
            theSession.Query<Target>().Where(x => num == x.Number).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(2);
        }

        [Fact]
        public void start_with_member_variable()
        {
            theSession.Store(new Target{Number = 1});
            theSession.Store(new Target{Number = 2});

            theSession.SaveChanges();
            var obj = new {Number = 2};
            theSession.Query<Target>().Where(x => obj.Number == x.Number).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(2);
        }
    }
}
