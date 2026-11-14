using System;
using System.Threading.Tasks;
using EventSourcingTests.FetchForWriting;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Weasel.Core;
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

    [Fact]
    public async Task rebuild_an_up_to_date_revisioned_aggregate_by_string_identity()
    {
        // #5788: Store() now honours a numeric revision, and the string-keyed overloads of
        // RebuildSingleStreamAsync Store() the rebuilt aggregate with concurrency checks ON (the Guid ones
        // disable them). The inline snapshot already sits at revision 3 and the rebuilt aggregate carries 3,
        // which a plain revisioned document would refuse (3 is not > 3). It is accepted because a projected
        // document's guard compares against the STREAM version (stream <= requested), not the stored row --
        // this pins that a rebuild of an up-to-date aggregate keeps working after #5788.
        // Its own schema: the shared store is Guid-keyed, and switching it to string stream identity
        // collides with the existing event tables.
        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "rebuild_revisioned_string";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<RevisionedStringAggregate5788>(SnapshotLifecycle.Inline);
        });

        var streamKey = Guid.NewGuid().ToString();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(streamKey, new AEvent(), new AEvent(), new BEvent());
            await session.SaveChangesAsync();
        }

        await store.Advanced.RebuildSingleStreamAsync<RevisionedStringAggregate5788>(streamKey);
        await store.Advanced.RebuildSingleStreamAsync<RevisionedStringAggregate5788>(streamKey, StorageConstants.DefaultTenantId);

        await using var query = store.QuerySession();
        var loaded = await query.LoadAsync<RevisionedStringAggregate5788>(streamKey);
        loaded.ShouldNotBeNull();
        loaded.ACount.ShouldBe(2);
        loaded.BCount.ShouldBe(1);
    }
}

public class RevisionedStringAggregate5788: IRevisioned
{
    public string Id { get; set; } = "";
    public int Version { get; set; }
    public int ACount { get; set; }
    public int BCount { get; set; }

    public void Apply(AEvent _) => ACount++;
    public void Apply(BEvent _) => BCount++;
}
