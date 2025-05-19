using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace LinqTests.Operators;

public class query_with_modulo_Tests : IntegrationContext
{
    #region sample_querying-with-modulo
    [Fact]
    public async Task use_modulo()
    {
        theSession.Store(new Target{Color = Colors.Blue, Number = 1});
        theSession.Store(new Target{Color = Colors.Blue, Number = 2});
        theSession.Store(new Target{Color = Colors.Blue, Number = 3});
        theSession.Store(new Target{Color = Colors.Blue, Number = 4});
        theSession.Store(new Target{Color = Colors.Blue, Number = 5});
        theSession.Store(new Target{Color = Colors.Green, Number = 6});

        await theSession.SaveChangesAsync();

        theSession.Query<Target>().Where(x => x.Number % 2 == 0 && x.Color < Colors.Green).ToArray()
            .Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(2, 4);
    }
    #endregion

    [Fact]
    public async Task use_modulo_operands_reversed()
    {
        theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        theSession.Store(new Target { Color = Colors.Blue, Number = 2 });
        theSession.Store(new Target { Color = Colors.Blue, Number = 3 });
        theSession.Store(new Target { Color = Colors.Blue, Number = 4 });
        theSession.Store(new Target { Color = Colors.Blue, Number = 5 });
        theSession.Store(new Target { Color = Colors.Green, Number = 6 });

        await theSession.SaveChangesAsync();

        theSession.Query<Target>().Where(x => 0 == x.Number % 2 && Colors.Green > x.Color).ToArray()
            .Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(2, 4);
    }

    public query_with_modulo_Tests(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
