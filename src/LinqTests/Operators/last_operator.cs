using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

public class last_operator: IntegrationContext
{
    [Fact]
    public async Task last_throws_an_exception()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        Should.Throw<InvalidOperationException>(() =>
        {
            theSession.Query<Target>().Last(x => x.Number == 3).ShouldNotBeNull();
        });
    }

    [Fact]
    public async Task last_or_default_throws_an_exception()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        Should.Throw<InvalidOperationException>(() =>
        {
            theSession.Query<Target>().Last(x => x.Number == 3).ShouldNotBeNull();
        });
    }


    public last_operator(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
