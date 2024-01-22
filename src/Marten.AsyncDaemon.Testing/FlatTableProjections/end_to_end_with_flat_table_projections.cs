using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Projections;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing.FlatTableProjections;

public class end_to_end_with_flat_table_projections : DaemonContext
{
    public end_to_end_with_flat_table_projections(ITestOutputHelper output) : base(output)
    {
        StoreOptions(opts => opts.Projections.Add<WriteTableWithGuidIdentifierProjection>(ProjectionLifecycle.Async));
    }

    [Fact]
    public async Task run_asynchronously()
    {
        var streamId = Guid.NewGuid();
        TheSession.Events.Append(streamId, new EventSourcingTests.Projections.Flattened.ValuesSet { A = 10, B = 10, C = 10, D = 10 });

        await TheSession.SaveChangesAsync();

        var valuesAdded = new EventSourcingTests.Projections.Flattened.ValuesSubtracted
        {
            A = 3, B = 4, C = 5, D = 6
        };

        TheSession.Events.Append(streamId, valuesAdded);

        await TheSession.SaveChangesAsync();

        using var daemon = await TheStore.BuildProjectionDaemonAsync();

        var waiter = daemon.Tracker.WaitForShardState("Values:All", 2);

        await daemon.RebuildProjection<WriteTableWithGuidIdentifierProjection>(CancellationToken.None);

        await waiter;
    }


}
