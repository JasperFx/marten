#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events.Tags;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
/// Parallel of <see cref="assign_tag_where_tests"/> for <see cref="DcbStorageMode.HStore"/>.
/// The retroactive-tag path uses <see cref="Marten.Events.Operations.AssignTagWhereHstoreOperation"/>
/// which emits <c>UPDATE mt_events SET tags = COALESCE(tags, ''::hstore) || hstore(...)</c>
/// against rows matching the user-supplied WHERE clause. The merge is naturally
/// idempotent (re-applying the same key-value yields the same hstore).
/// Reuses <see cref="RegionId"/>, <see cref="OrderPlaced"/>, <see cref="OrderShipped"/>,
/// <see cref="OrderCancelled"/> from <c>assign_tag_where_tests.cs</c>.
/// </summary>
[Collection("OneOffs")]
public class hstore_assign_tag_where_tests: OneOffConfigurationsContext, IAsyncLifetime
{
    private RegionId _eastRegion = null!;
    private RegionId _westRegion = null!;

    public Task InitializeAsync()
    {
        _eastRegion = new RegionId(Guid.NewGuid());
        _westRegion = new RegionId(Guid.NewGuid());

        StoreOptions(opts =>
        {
            opts.Events.AddEventType<OrderPlaced>();
            opts.Events.AddEventType<OrderShipped>();
            opts.Events.AddEventType<OrderCancelled>();

            opts.Events.DcbStorageMode = DcbStorageMode.HStore;
            opts.Events.RegisterTagType<RegionId>("region");
            opts.Events.RegisterTagType<StudentId>("student");
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task assign_tag_where_by_event_type_name()
    {
        var stream1 = Guid.NewGuid();
        theSession.Events.Append(stream1,
            new OrderPlaced("ORD-1", 100m),
            new OrderShipped("ORD-1"));
        await theSession.SaveChangesAsync();

        await using var session2 = theStore.LightweightSession();
        var orderPlacedTypeName = theStore.Options.EventGraph.EventMappingFor<OrderPlaced>().EventTypeName;
        session2.Events.AssignTagWhere(
            e => e.EventTypeName == orderPlacedTypeName,
            _eastRegion);
        await session2.SaveChangesAsync();

        await using var session3 = theStore.LightweightSession();
        var query = new EventTagQuery().Or<RegionId>(_eastRegion);
        var events = await session3.Events.QueryByTagsAsync(query);

        events.Count.ShouldBe(1);
        events[0].Data.ShouldBeOfType<OrderPlaced>().OrderNumber.ShouldBe("ORD-1");
    }

    [Fact]
    public async Task assign_tag_where_by_stream_id()
    {
        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        theSession.Events.Append(stream1,
            new OrderPlaced("ORD-1", 100m),
            new OrderShipped("ORD-1"));
        theSession.Events.Append(stream2,
            new OrderPlaced("ORD-2", 200m));
        await theSession.SaveChangesAsync();

        await using var session2 = theStore.LightweightSession();
        session2.Events.AssignTagWhere(
            e => e.StreamId == stream1,
            _eastRegion);
        await session2.SaveChangesAsync();

        await using var session3 = theStore.LightweightSession();
        var query = new EventTagQuery().Or<RegionId>(_eastRegion);
        var events = await session3.Events.QueryByTagsAsync(query);

        events.Count.ShouldBe(2);
        events.ShouldAllBe(e => e.StreamId == stream1);
    }

    [Fact]
    public async Task assign_tag_where_with_compound_predicate()
    {
        var stream1 = Guid.NewGuid();

        theSession.Events.Append(stream1,
            new OrderPlaced("ORD-1", 100m),
            new OrderShipped("ORD-1"),
            new OrderCancelled("ORD-1", "changed mind"));
        await theSession.SaveChangesAsync();

        await using var session2 = theStore.LightweightSession();
        var placedType = theStore.Options.EventGraph.EventMappingFor<OrderPlaced>().EventTypeName;
        var cancelledType = theStore.Options.EventGraph.EventMappingFor<OrderCancelled>().EventTypeName;

        session2.Events.AssignTagWhere(
            e => e.EventTypeName == placedType || e.EventTypeName == cancelledType,
            _eastRegion);
        await session2.SaveChangesAsync();

        await using var session3 = theStore.LightweightSession();
        var query = new EventTagQuery().Or<RegionId>(_eastRegion);
        var events = await session3.Events.QueryByTagsAsync(query);

        events.Count.ShouldBe(2);
        events.Select(e => e.Data.GetType()).ShouldContain(typeof(OrderPlaced));
        events.Select(e => e.Data.GetType()).ShouldContain(typeof(OrderCancelled));
        events.Select(e => e.Data.GetType()).ShouldNotContain(typeof(OrderShipped));
    }

    [Fact]
    public async Task assign_tag_where_is_idempotent()
    {
        // In HStore mode idempotency comes from `tags = coalesce(tags, ''::hstore) || hstore(k,v)` —
        // re-applying the same kv yields the same hstore, no duplicate tag rows can exist by construction.
        var stream1 = Guid.NewGuid();
        theSession.Events.Append(stream1, new OrderPlaced("ORD-1", 100m));
        await theSession.SaveChangesAsync();

        var placedType = theStore.Options.EventGraph.EventMappingFor<OrderPlaced>().EventTypeName;

        await using var session2 = theStore.LightweightSession();
        session2.Events.AssignTagWhere(
            e => e.EventTypeName == placedType, _eastRegion);
        await session2.SaveChangesAsync();

        await using var session3 = theStore.LightweightSession();
        session3.Events.AssignTagWhere(
            e => e.EventTypeName == placedType, _eastRegion);
        await session3.SaveChangesAsync();

        await using var session4 = theStore.LightweightSession();
        var query = new EventTagQuery().Or<RegionId>(_eastRegion);
        var events = await session4.Events.QueryByTagsAsync(query);
        events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task assign_tag_where_does_not_affect_unmatched_events()
    {
        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        theSession.Events.Append(stream1, new OrderPlaced("ORD-1", 100m));
        theSession.Events.Append(stream2, new OrderPlaced("ORD-2", 200m));
        await theSession.SaveChangesAsync();

        await using var session2 = theStore.LightweightSession();
        session2.Events.AssignTagWhere(
            e => e.StreamId == stream1, _eastRegion);
        await session2.SaveChangesAsync();

        await using var session3 = theStore.LightweightSession();
        session3.Events.AssignTagWhere(
            e => e.StreamId == stream2, _westRegion);
        await session3.SaveChangesAsync();

        await using var session4 = theStore.LightweightSession();
        var eastEvents = await session4.Events.QueryByTagsAsync(
            new EventTagQuery().Or<RegionId>(_eastRegion));
        eastEvents.Count.ShouldBe(1);
        eastEvents[0].StreamId.ShouldBe(stream1);

        var westEvents = await session4.Events.QueryByTagsAsync(
            new EventTagQuery().Or<RegionId>(_westRegion));
        westEvents.Count.ShouldBe(1);
        westEvents[0].StreamId.ShouldBe(stream2);
    }

    [Fact]
    public async Task assign_tag_where_merges_across_different_tag_types_on_same_event()
    {
        // HStore-specific cross-type merge: retroactively assigning a tag of one type
        // (RegionId) and then a tag of a DIFFERENT type (StudentId) to the same event
        // must merge both keys into the row's hstore rather than overwrite. This is
        // structurally impossible to break in TagTables mode (each tag type lives in
        // its own table) but is a real risk in HStore where a naive UPDATE would
        // replace the entire column. Validated via the `tags || hstore(...)` form.
        var stream1 = Guid.NewGuid();
        theSession.Events.Append(stream1, new OrderPlaced("ORD-1", 100m));
        await theSession.SaveChangesAsync();

        var placedType = theStore.Options.EventGraph.EventMappingFor<OrderPlaced>().EventTypeName;
        var studentTag = new StudentId(Guid.NewGuid());

        await using var session2 = theStore.LightweightSession();
        session2.Events.AssignTagWhere(e => e.EventTypeName == placedType, _eastRegion);
        await session2.SaveChangesAsync();

        await using var session3 = theStore.LightweightSession();
        session3.Events.AssignTagWhere(e => e.EventTypeName == placedType, studentTag);
        await session3.SaveChangesAsync();

        // Both tag types should still find the event — proving the second
        // AssignTagWhere preserved the first one's key in the hstore.
        await using var session4 = theStore.LightweightSession();
        (await session4.Events.QueryByTagsAsync(
            new EventTagQuery().Or<RegionId>(_eastRegion))).Count.ShouldBe(1);
        (await session4.Events.QueryByTagsAsync(
            new EventTagQuery().Or<StudentId>(studentTag))).Count.ShouldBe(1);
    }

    [Fact]
    public async Task assign_tag_where_overwrites_same_tag_type_in_hstore_mode()
    {
        // HStore semantic divergence from TagTables: each hstore key is unique, so
        // retroactively assigning a second value of the SAME tag type to the same
        // event overwrites the prior value rather than adding a second row. Tested
        // here to make the divergence explicit and pin the contract: HStore mode
        // stores at most one tag value per (event, tag type) pair.
        //
        // TagTables mode allows two values of the same tag type on the same event
        // because the underlying table PK is (value, seq_id).
        var stream1 = Guid.NewGuid();
        theSession.Events.Append(stream1, new OrderPlaced("ORD-1", 100m));
        await theSession.SaveChangesAsync();

        var placedType = theStore.Options.EventGraph.EventMappingFor<OrderPlaced>().EventTypeName;

        await using var session2 = theStore.LightweightSession();
        session2.Events.AssignTagWhere(e => e.EventTypeName == placedType, _eastRegion);
        await session2.SaveChangesAsync();

        await using var session3 = theStore.LightweightSession();
        session3.Events.AssignTagWhere(e => e.EventTypeName == placedType, _westRegion);
        await session3.SaveChangesAsync();

        // East was overwritten by west — only west finds the event in HStore mode.
        await using var session4 = theStore.LightweightSession();
        (await session4.Events.QueryByTagsAsync(
            new EventTagQuery().Or<RegionId>(_eastRegion))).Count.ShouldBe(0);
        (await session4.Events.QueryByTagsAsync(
            new EventTagQuery().Or<RegionId>(_westRegion))).Count.ShouldBe(1);
    }

    [Fact]
    public void assign_tag_where_throws_for_unregistered_tag_type()
    {
        // CourseId is intentionally NOT registered on this fixture — only RegionId
        // and StudentId are — so passing one through AssignTagWhere must throw.
        var unregisteredTag = new CourseId(Guid.NewGuid());

        Should.Throw<InvalidOperationException>(() =>
        {
            theSession.Events.AssignTagWhere(e => e.Sequence > 0, unregisteredTag);
        });
    }
}
