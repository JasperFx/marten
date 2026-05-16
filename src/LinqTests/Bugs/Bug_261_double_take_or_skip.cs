using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Marten;

namespace LinqTests.Bugs;

public class Bug_261_double_take_or_skip: IntegrationContext
{
    [Fact]
    public async Task does_not_blow_up_with_double_take()
    {
        var targets = Target.GenerateRandomData(100);
        await theStore.BulkInsertAsync(targets.ToArray());

        (await theSession.Query<Target>().Take(4).Take(8).ToListAsync())
            .Count.ShouldBe(8);
    }

    [Fact]
    public async Task does_not_blow_up_with_double_skip()
    {
        var targets = Target.GenerateRandomData(100);
        await theStore.BulkInsertAsync(targets.ToArray());

        (await theSession.Query<Target>().Take(4).Skip(4).Skip(8).ToListAsync())
            .Count.ShouldBe(4);
    }

    [Fact]
    public async Task one_more_try()
    {
        var targets = Target.GenerateRandomData(100);
        await theStore.BulkInsertAsync(targets.ToArray());

        var result = (await theSession.Query<Target>().Take(10).Take(4).ToListAsync());
        result.Count.ShouldBe(4);
    }

    protected override async Task fixtureSetup()
    {
        await theStore.Advanced.ResetAllData();
    }

    public Bug_261_double_take_or_skip(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
