using System.Linq;
using System.Threading.Tasks;
using Marten.PLv8.Patching;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.PLv8.Testing.Patching;

public class Bug_2170_patch_with_sub_collection_query : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_2170_patch_with_sub_collection_query(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task work_correctly()
    {
        StoreOptions(opts =>
        {
            opts.Logger(new TestOutputMartenLogger(_output));
            opts.UseJavascriptTransformsAndPatching();
        });

        var targets = Target.GenerateRandomData(50).ToArray();

        await TheStore.BulkInsertAsync(targets);

        var initialCount = targets.Count(x => x.Inner.Children != null && x.Inner.Children.Any(t => t.Color == Colors.Blue));
        targets.Length.ShouldNotBe(initialCount);

        TheSession.Patch<Target>(x => x.Inner.Children != null && x.Inner.Children.Any(t => t.Color == Colors.Blue)).Set(x => x.Long, 33333);

        await TheSession.SaveChangesAsync();

        var children =
            targets.Where(x => x.Inner.Children != null && x.Inner.Children.Any(t => t.Color == Colors.Blue)).Select(x => x.Id).ToArray();

        var values = await TheSession.LoadManyAsync<Target>(children);

        foreach (var value in values)
        {
            value.Long.ShouldBe(33333);
        }
    }
}
