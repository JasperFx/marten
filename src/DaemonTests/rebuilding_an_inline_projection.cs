using System.Linq;
using System.Threading.Tasks;
using Bobcat;
using DaemonTests.EventProjections;
using DaemonTests.TestingSupport;
using JasperFx.Events.Projections;
using Shouldly;
using Xunit;

namespace DaemonTests;

[BobcatFeature("Rebuilding an inline projection")]
public class rebuilding_an_inline_projection : DaemonContext
{
    public rebuilding_an_inline_projection(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task do_not_leave_behind_progressions()
    {
        StoreOptions(x => x.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Inline));
        NumberOfStreams = 10;
        await PublishSingleThreaded();

        var agent = await StartDaemon();

        await RebuildProjection(agent, "Distance");

        // Then no Distance progression is left behind
        var progress = await theStore.Advanced.AllProjectionProgress(token: TestContext.Current.CancellationToken);
        progress.Any(x => x.ShardName.StartsWith("Distance")).ShouldBeFalse();


    }
}
