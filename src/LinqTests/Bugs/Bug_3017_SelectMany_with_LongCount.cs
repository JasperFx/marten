using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_3017_SelectMany_with_LongCount : BugIntegrationContext
{
    [Fact]
    public async Task can_use_select_many_with_long_count()
    {
        var targets = Target.GenerateRandomData(1000).ToArray();

        await theStore.BulkInsertDocumentsAsync(targets);

        var expected = targets.SelectMany(x => x.Children).LongCount();

        var actual = await theSession.Query<Target>().SelectMany(x => x.Children)
            .LongCountAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task can_use_select_many_with_integer_count()
    {
        var targets = Target.GenerateRandomData(1000).ToArray();

        await theStore.BulkInsertDocumentsAsync(targets);

        var expected = targets.SelectMany(x => x.Children).Count();

        var actual = await theSession.Query<Target>().SelectMany(x => x.Children)
            .CountAsync();

        actual.ShouldBe(expected);
    }
}
