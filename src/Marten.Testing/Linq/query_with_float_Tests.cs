using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Linq
{
    [ControlledQueryStoryteller]
    public class query_with_float_Tests : IntegrationContextWithIdentityMap<NulloIdentityMap>
    {
        [Fact]
        public void can_query_by_float()
        {
            var target1 = new Target {Float = 123.45F};
            var target2 = new Target {Float = 456.45F};

            theSession.Store(target1, target2);
            theSession.Store(Target.GenerateRandomData(5).ToArray());

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Float > 400).ToArray().Select(x => x.Id)
                .ShouldContain(x => x == target2.Id);
        }

        public query_with_float_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
