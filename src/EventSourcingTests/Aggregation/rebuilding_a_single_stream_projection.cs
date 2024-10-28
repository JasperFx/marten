using System;
using System.Threading.Tasks;
using EventSourcingTests.FetchForWriting;
using Marten.Events;
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
}

