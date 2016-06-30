using System.Linq;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Linq
{
    public class query_with_modulo_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        // SAMPLE: querying-with-modulo
        [Fact]
        public void use_modulo()
        {
            theSession.Store(new Target{Color = Colors.Blue, Number = 1});
            theSession.Store(new Target{Color = Colors.Blue, Number = 2});
            theSession.Store(new Target{Color = Colors.Blue, Number = 3});
            theSession.Store(new Target{Color = Colors.Blue, Number = 4});
            theSession.Store(new Target{Color = Colors.Blue, Number = 5});
            theSession.Store(new Target{Color = Colors.Green, Number = 6});

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Number % 2 == 0 && x.Color == Colors.Blue).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(2, 4);
        }
        // ENDSAMPLE
    }
}