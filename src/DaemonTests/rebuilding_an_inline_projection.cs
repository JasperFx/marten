using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.EventProjections;
using DaemonTests.TestingSupport;
using JasperFx.Events.Projections;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests;

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
        await agent.RebuildProjectionAsync("Distance", CancellationToken.None);

        var progress = await theStore.Advanced.AllProjectionProgress();
        progress.Any(x => x.ShardName.StartsWith("Distance")).ShouldBeFalse();


    }
}
