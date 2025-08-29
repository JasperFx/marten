using System;
using System.Threading.Tasks;
using EventSourcingTests.FetchForWriting;
using JasperFx.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class rebuilding_a_single_stream_projection : IntegrationContext
{
    public rebuilding_a_single_stream_projection(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task rebuild_by_guid_identity()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream(streamId, new AEvent(), new AEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new BEvent(), new BEvent());
        theSession.Events.Append(streamId, new CEvent(), new CEvent(), new CEvent());
        theSession.Events.Append(streamId, new DEvent());

        await theSession.SaveChangesAsync();

        #region sample_rebuild_single_stream

        await theStore.Advanced.RebuildSingleStreamAsync<SimpleAggregate>(streamId);

            #endregion

        // Note that you will still have to explicitly commit the changes!
        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<SimpleAggregate>(streamId);
        loaded.ACount.ShouldBe(4);
        loaded.BCount.ShouldBe(2);
        loaded.CCount.ShouldBe(3);
        loaded.DCount.ShouldBe(1);
    }


    [Fact]
    public async Task rebuild_by_string_identity()
    {
        this.UseStreamIdentity(StreamIdentity.AsString);

        var streamKey = Guid.NewGuid().ToString();
        theSession.Events.StartStream(streamKey, new AEvent(), new AEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamKey, new BEvent(), new BEvent());
        theSession.Events.Append(streamKey, new CEvent(), new CEvent(), new CEvent());
        theSession.Events.Append(streamKey, new DEvent());

        await theSession.SaveChangesAsync();

        await theStore.Advanced.RebuildSingleStreamAsync<SimpleAggregateAsString>(streamKey);


        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<SimpleAggregateAsString>(streamKey);
        loaded.ACount.ShouldBe(4);
        loaded.BCount.ShouldBe(2);
        loaded.CCount.ShouldBe(3);
        loaded.DCount.ShouldBe(1);
    }

    [Fact]
    public async Task rebuild_existing_aggregate()
    {
        // In case of code changes to an aggregate, it may be necessary to rebuild. This test ensures that data is
        // updated.

        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline));

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream(streamId, new AEvent());
        await theSession.SaveChangesAsync();

        // Modify some data on the aggregate so we can verify that it is changed back later.
        var aggregate = await theSession.LoadAsync<SimpleAggregate>(streamId);
        var originalACount = aggregate.ACount;
        aggregate.ACount += 1;
        theSession.Delete<SimpleAggregate>(streamId);
        theSession.Store(aggregate);
        await theSession.SaveChangesAsync();

        // Rebuild
        await theStore.Advanced.RebuildSingleStreamAsync<SimpleAggregate>(streamId);
        var rebuiltAggregate = await theSession.LoadAsync<SimpleAggregate>(streamId);

        // Verify that the modified data has changed back to the original aggregated value
        rebuiltAggregate.ACount.ShouldBe(originalACount);
    }
}

