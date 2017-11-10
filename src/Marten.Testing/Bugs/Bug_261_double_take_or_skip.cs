using System.Linq;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    [ControlledQueryStoryteller]
    public class Bug_261_double_take_or_skip : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void does_not_blow_up_with_double_take()
        {
            var targets = Target.GenerateRandomData(100);
            theStore.BulkInsert(targets.ToArray());

            theSession.Query<Target>().Take(4).Take(8).ToList()
                .Count.ShouldBe(8);
        }

        [Fact]
        public void does_not_blow_up_with_double_skip()
        {
            var targets = Target.GenerateRandomData(100);
            theStore.BulkInsert(targets.ToArray());

            theSession.Query<Target>().Take(4).Skip(4).Skip(8).ToList()
                .Count.ShouldBe(4);
        }

        [Fact]
        public void one_more_try()
        {
            var targets = Target.GenerateRandomData(100);
            theStore.BulkInsert(targets.ToArray());

            var result = theSession.Query<Target>().Take(10).Take(4).ToList();
            result.Count.ShouldBe(4);
        }
    }
}