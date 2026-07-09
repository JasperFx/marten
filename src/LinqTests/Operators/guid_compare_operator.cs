using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Marten;

namespace LinqTests.Operators;

// Guid has no < operator, so Guid.CompareTo is the only LINQ-expressible relational comparison.
public class guid_compare_operator: IntegrationContext
{
    [Fact]
    public async Task guid_compare_to_works()
    {
        var g1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var g2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var g3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

        theSession.Store(new Target { OtherGuid = g1 });
        theSession.Store(new Target { OtherGuid = g2 });
        theSession.Store(new Target { OtherGuid = g3 });
        await theSession.SaveChangesAsync();

        var results = await theSession.Query<Target>()
            .Where(x => x.OtherGuid.CompareTo(g3) < 0)
            .ToListAsync();

        results.Select(x => x.OtherGuid).ShouldBe(new[] { g1, g2 }, ignoreOrder: true);
    }

    protected override async Task fixtureSetup()
    {
        await theStore.Advanced.ResetAllData();
    }

    public guid_compare_operator(DefaultStoreFixture fixture): base(fixture)
    {
    }
}
