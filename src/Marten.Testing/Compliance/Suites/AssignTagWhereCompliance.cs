using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events.Tags;
using Shouldly;
using Xunit;

namespace JasperFx.Events.ComplianceTests;

public record RegionId(Guid Value);

/// <summary>
/// A tag type that is deliberately never registered with the store.
/// </summary>
public record UnregisteredTagId(Guid Value);

public record OrderPlaced(string OrderNumber, decimal Amount);

public record OrderShipped(string OrderNumber);

public record OrderCancelled(string OrderNumber, string Reason);

/// <summary>
/// Retroactive tagging: AssignTagWhere applies a DCB tag to already-persisted events matching a
/// predicate over the event metadata.
/// </summary>
public abstract class AssignTagWhereCompliance<TFixture, TOperations, TQuerySession>
    : EventStoreComplianceSuite<TFixture, TOperations, TQuerySession>
    where TFixture : EventStoreComplianceFixture<TOperations, TQuerySession>, new()
    where TOperations : TQuerySession, IStorageOperations
{
    private static readonly Action<ComplianceStoreConfig> _configuration = config =>
    {
        config.SchemaName = "compliance_assign_tag";

        config.AddEventType<OrderPlaced>();
        config.AddEventType<OrderShipped>();
        config.AddEventType<OrderCancelled>();

        config.RegisterTagType<RegionId>("region");
    };

    protected override Action<ComplianceStoreConfig> Configuration => _configuration;

    private readonly RegionId _eastRegion = new(Guid.NewGuid());
    private readonly RegionId _westRegion = new(Guid.NewGuid());

    [Fact]
    public async Task assign_tag_where_by_event_type_name()
    {
        var stream1 = Guid.NewGuid();

        await using var session1 = OpenSession();
        EventsFor(session1).Append(stream1, new OrderPlaced("ORD-1", 100m), new OrderShipped("ORD-1"));
        await SaveChangesAsync(session1);

        // Retroactively tag every OrderPlaced event with a region
        await using var session2 = OpenSession();
        var orderPlacedTypeName = EventTypeNameFor<OrderPlaced>();
        EventsFor(session2).AssignTagWhere(e => e.EventTypeName == orderPlacedTypeName, _eastRegion);
        await SaveChangesAsync(session2);

        await using var session3 = OpenSession();
        var query = new EventTagQuery().Or<RegionId>(_eastRegion);
        var events = await EventsFor(session3).QueryByTagsAsync(query, Cancellation);

        events.Count.ShouldBe(1);
        events[0].Data.ShouldBeOfType<OrderPlaced>().OrderNumber.ShouldBe("ORD-1");
    }

    [Fact]
    public async Task assign_tag_where_by_stream_id()
    {
        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        await using var session1 = OpenSession();
        EventsFor(session1).Append(stream1, new OrderPlaced("ORD-1", 100m), new OrderShipped("ORD-1"));
        EventsFor(session1).Append(stream2, new OrderPlaced("ORD-2", 200m));
        await SaveChangesAsync(session1);

        await using var session2 = OpenSession();
        EventsFor(session2).AssignTagWhere(e => e.StreamId == stream1, _eastRegion);
        await SaveChangesAsync(session2);

        await using var session3 = OpenSession();
        var query = new EventTagQuery().Or<RegionId>(_eastRegion);
        var events = await EventsFor(session3).QueryByTagsAsync(query, Cancellation);

        events.Count.ShouldBe(2);
        events.ShouldAllBe(e => e.StreamId == stream1);
    }

    [Fact]
    public async Task assign_tag_where_with_compound_predicate()
    {
        var stream1 = Guid.NewGuid();

        await using var session1 = OpenSession();
        EventsFor(session1).Append(stream1,
            new OrderPlaced("ORD-1", 100m),
            new OrderShipped("ORD-1"),
            new OrderCancelled("ORD-1", "changed mind"));
        await SaveChangesAsync(session1);

        await using var session2 = OpenSession();
        var placedType = EventTypeNameFor<OrderPlaced>();
        var cancelledType = EventTypeNameFor<OrderCancelled>();

        EventsFor(session2).AssignTagWhere(
            e => e.EventTypeName == placedType || e.EventTypeName == cancelledType,
            _eastRegion);
        await SaveChangesAsync(session2);

        await using var session3 = OpenSession();
        var query = new EventTagQuery().Or<RegionId>(_eastRegion);
        var events = await EventsFor(session3).QueryByTagsAsync(query, Cancellation);

        events.Count.ShouldBe(2);
        events.Select(e => e.Data.GetType()).ShouldContain(typeof(OrderPlaced));
        events.Select(e => e.Data.GetType()).ShouldContain(typeof(OrderCancelled));
        events.Select(e => e.Data.GetType()).ShouldNotContain(typeof(OrderShipped));
    }

    [Fact]
    public async Task assign_tag_where_is_idempotent()
    {
        var stream1 = Guid.NewGuid();

        await using var session1 = OpenSession();
        EventsFor(session1).Append(stream1, new OrderPlaced("ORD-1", 100m));
        await SaveChangesAsync(session1);

        var placedType = EventTypeNameFor<OrderPlaced>();

        await using var session2 = OpenSession();
        EventsFor(session2).AssignTagWhere(e => e.EventTypeName == placedType, _eastRegion);
        await SaveChangesAsync(session2);

        await using var session3 = OpenSession();
        EventsFor(session3).AssignTagWhere(e => e.EventTypeName == placedType, _eastRegion);
        await SaveChangesAsync(session3);

        await using var session4 = OpenSession();
        var query = new EventTagQuery().Or<RegionId>(_eastRegion);
        var events = await EventsFor(session4).QueryByTagsAsync(query, Cancellation);
        events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task assign_tag_where_does_not_affect_unmatched_events()
    {
        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        await using var session1 = OpenSession();
        EventsFor(session1).Append(stream1, new OrderPlaced("ORD-1", 100m));
        EventsFor(session1).Append(stream2, new OrderPlaced("ORD-2", 200m));
        await SaveChangesAsync(session1);

        await using var session2 = OpenSession();
        EventsFor(session2).AssignTagWhere(e => e.StreamId == stream1, _eastRegion);
        await SaveChangesAsync(session2);

        await using var session3 = OpenSession();
        EventsFor(session3).AssignTagWhere(e => e.StreamId == stream2, _westRegion);
        await SaveChangesAsync(session3);

        await using var session4 = OpenSession();
        var eastEvents = await EventsFor(session4)
            .QueryByTagsAsync(new EventTagQuery().Or<RegionId>(_eastRegion), Cancellation);
        eastEvents.Count.ShouldBe(1);
        eastEvents[0].StreamId.ShouldBe(stream1);

        var westEvents = await EventsFor(session4)
            .QueryByTagsAsync(new EventTagQuery().Or<RegionId>(_westRegion), Cancellation);
        westEvents.Count.ShouldBe(1);
        westEvents[0].StreamId.ShouldBe(stream2);
    }

    [Fact]
    public async Task assign_tag_where_throws_for_unregistered_tag_type()
    {
        await using var session = OpenSession();

        var unregisteredTag = new UnregisteredTagId(Guid.NewGuid());

        Should.Throw<InvalidOperationException>(() =>
            EventsFor(session).AssignTagWhere(e => e.Sequence > 0, unregisteredTag));
    }
}
