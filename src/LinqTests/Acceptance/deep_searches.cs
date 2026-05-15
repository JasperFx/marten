using System.Linq;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten;

namespace LinqTests.Acceptance;

public class deep_searches: OneOffConfigurationsContext
{
    [Fact]
    public async Task query_two_deep()
    {
        theSession.Store(new Target { Inner = new Target { Number = 1, String = "Jeremy" } });
        theSession.Store(new Target { Inner = new Target { Number = 2, String = "Max" } });
        theSession.Store(new Target { Inner = new Target { Number = 1, String = "Declan" } });
        theSession.Store(new Target { Inner = new Target { Number = 2, String = "Lindsey" } });
        theSession.Store(new Target { String = "Russell" });

        await theSession.SaveChangesAsync();

        (await theSession.Query<Target>().Where(x => x.Inner.Number == 2).ToListAsync()).OrderBy(x => x.Inner.String)
            .Select(x => x.Inner.String)
            .ShouldHaveTheSameElementsAs("Lindsey", "Max");
    }

    [Fact]
    public async Task query_three_deep()
    {
        theSession.Store(new Target { Number = 1, Inner = new Target { Inner = new Target { Long = 1 } } });
        theSession.Store(new Target { Number = 2, Inner = new Target { Inner = new Target { Long = 2 } } });
        theSession.Store(new Target { Number = 3, Inner = new Target { Inner = new Target { Long = 1 } } });
        theSession.Store(new Target { Number = 4, Inner = new Target { Inner = new Target { Long = 2 } } });

        await theSession.SaveChangesAsync();

        (await theSession.Query<Target>().Where(x => x.Inner.Inner.Long == 1).ToListAsync()).Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(1, 3);
    }

    [Fact]
    public async Task order_by_2_deep()
    {
        theSession.Store(new Target { Inner = new Target { Number = 1, String = "Jeremy" } });
        theSession.Store(new Target { Inner = new Target { Number = 2, String = "Max" } });
        theSession.Store(new Target { Inner = new Target { Number = 1, String = "Declan" } });
        theSession.Store(new Target { Inner = new Target { Number = 2, String = "Lindsey" } });
        theSession.Store(new Target { String = "Russell" });

        await theSession.SaveChangesAsync();

        (await theSession.Query<Target>().Where(x => x.Inner.Number == 2).OrderBy(x => x.Inner.String).ToListAsync())
            .Select(x => x.Inner.String)
            .ShouldHaveTheSameElementsAs("Lindsey", "Max");
    }

    [Fact]
    public async Task query_two_deep_with_containment_operator()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().PropertySearching(PropertySearching.ContainmentOperator);
        });


        theSession.Store(new Target { Inner = new Target { Number = 1, String = "Jeremy" } });
        theSession.Store(new Target { Inner = new Target { Number = 2, String = "Max" } });
        theSession.Store(new Target { Inner = new Target { Number = 1, String = "Declan" } });
        theSession.Store(new Target { Inner = new Target { Number = 2, String = "Lindsey" } });
        theSession.Store(new Target { String = "Russell" });

        await theSession.SaveChangesAsync();

        (await theSession.Query<Target>().Where(x => x.Inner.Number == 2).ToListAsync()).OrderBy(x => x.Inner.String)
            .Select(x => x.Inner.String)
            .ShouldHaveTheSameElementsAs("Lindsey", "Max");
    }

    [Fact]
    public async Task query_three_deep_with_containment_operator()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().PropertySearching(PropertySearching.ContainmentOperator);
        });

        theSession.Store(new Target { Number = 1, Inner = new Target { Inner = new Target { Long = 1 } } });
        theSession.Store(new Target { Number = 2, Inner = new Target { Inner = new Target { Long = 2 } } });
        theSession.Store(new Target { Number = 3, Inner = new Target { Inner = new Target { Long = 1 } } });
        theSession.Store(new Target { Number = 4, Inner = new Target { Inner = new Target { Long = 2 } } });

        await theSession.SaveChangesAsync();

        (await theSession.Query<Target>().Where(x => x.Inner.Inner.Long == 1).ToListAsync()).Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(1, 3);
    }

    [Fact]
    public async Task order_by_2_deep_with_containment_operator()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().PropertySearching(PropertySearching.ContainmentOperator);
        });

        theSession.Store(new Target { Inner = new Target { Number = 1, String = "Jeremy" } });
        theSession.Store(new Target { Inner = new Target { Number = 2, String = "Max" } });
        theSession.Store(new Target { Inner = new Target { Number = 1, String = "Declan" } });
        theSession.Store(new Target { Inner = new Target { Number = 2, String = "Lindsey" } });
        theSession.Store(new Target { String = "Russell" });

        await theSession.SaveChangesAsync();

        (await theSession.Query<Target>().Where(x => x.Inner.Number == 2).OrderBy(x => x.Inner.String).ToListAsync())
            .Select(x => x.Inner.String)
            .ShouldHaveTheSameElementsAs("Lindsey", "Max");
    }

}
