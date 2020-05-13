using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Linq
{
    [ControlledQueryStoryteller]
    public class query_with_modulo_Tests : IntegrationContext
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

            theSession.Query<Target>().Where(x => x.Number % 2 == 0 && x.Color < Colors.Green).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(2, 4);
        }
        // ENDSAMPLE

        [Fact]
        public void use_modulo_operands_reversed()
        {
            theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 2 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 3 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 4 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 5 });
            theSession.Store(new Target { Color = Colors.Green, Number = 6 });

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => 0 == x.Number % 2 && Colors.Green > x.Color).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(2, 4);
        }

        public query_with_modulo_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
