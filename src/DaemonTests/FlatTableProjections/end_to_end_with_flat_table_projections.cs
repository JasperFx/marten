using System;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.FlatTableProjections;

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
        theSession.Events.Append(streamId, new EventSourcingTests.Projections.Flattened.ValuesSet { A = 10, B = 10, C = 10, D = 10 });

        await theSession.SaveChangesAsync();

        var valuesSubtracted = new EventSourcingTests.Projections.Flattened.ValuesSubtracted
        {
            A = 3, B = 4, C = 5, D = 6, MaybeNumber = null
        };

        var valuesSubtracted2 = new EventSourcingTests.Projections.Flattened.ValuesSubtracted
        {
            A = 0, B = 0, C = 0, D = 0, MaybeNumber = 10
        };

        theSession.Events.Append(streamId, valuesSubtracted, valuesSubtracted2);

        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();

        var waiter = daemon.Tracker.WaitForShardState("Values:All", 2);

        await daemon.RebuildProjectionAsync<WriteTableWithGuidIdentifierProjection>(CancellationToken.None);

        await waiter;
    }


}
