using System.Linq;
using Marten.Services;
using Marten.Testing.Fixtures;
using Xunit;

namespace Marten.Testing.Linq
{
    public class query_with_enums_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void use_enum_values()
        {
            theSession.Store(new Target{Color = Colors.Blue, Number = 1});
            theSession.Store(new Target{Color = Colors.Red, Number = 2});
            theSession.Store(new Target{Color = Colors.Green, Number = 3});
            theSession.Store(new Target{Color = Colors.Blue, Number = 4});
            theSession.Store(new Target{Color = Colors.Red, Number = 5});
            theSession.Store(new Target{Color = Colors.Green, Number = 6});
            theSession.Store(new Target{Color = Colors.Blue, Number = 7});

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1, 4, 7);
        }
    }
}