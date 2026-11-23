using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

#region sample_natural_key_registered_order

public class RegisteredOrder
{
    public Guid Id { get; set; }

    [NaturalKey]
    public OrderNumber OrderNum { get; set; } = null!;

    public int Items { get; set; }

    public void Apply(OrderCreated e) => OrderNum = e.OrderNumber;

    public void Apply(OrderItemAdded e) => Items++;

    public void Apply(OrderNumberChanged e) => OrderNum = e.NewOrderNumber;
}

#endregion

#region sample_natural_key_explicit_registration_in_projection_class

public class RegisteredOrderProjection: SingleStreamProjection<RegisteredOrder, Guid>
{
    public RegisteredOrderProjection()
    {
        NaturalKeyFor(x => x
            .SetBy<OrderCreated>(e => e.OrderNumber)
            .SetBy<OrderNumberChanged>(e => e.NewOrderNumber));
    }
}

#endregion

#region sample_natural_key_sources_and_null

public record ShipmentCode(string Value);

public record ShipmentBooked(ShipmentCode Code);

public record ShipmentUpdated(ShipmentCode? NewCode, string Notes);

public record ShipmentCodeWithdrawn;

public class Shipment
{
    public Guid Id { get; set; }

    [NaturalKey]
    public ShipmentCode? Code { get; set; }

    public string Notes { get; set; } = string.Empty;

    [NaturalKeySource]
    public void Apply(ShipmentBooked e) => Code = e.Code;

    // Marten reads the key off the event here, so a ShipmentUpdated whose NewCode is null means
    // "this event carries no key", and the stream keeps its code
    [NaturalKeySource]
    public void Apply(ShipmentUpdated e)
    {
        if (e.NewCode != null) Code = e.NewCode;
        Notes = e.Notes;
    }

    // A static source that returns the key type is explicit, so its null removes the stream's key
    [NaturalKeySource]
    public static ShipmentCode? KeyFor(ShipmentCodeWithdrawn e) => null;

    public void Apply(ShipmentCodeWithdrawn e) => Code = null;
}

#endregion

/// <summary>
/// The code samples in docs/events/natural-keys.md that no other test shows.
/// </summary>
public class natural_key_documentation_samples: OneOffConfigurationsContext
{
    private async Task<Guid> startOrder(OrderNumber number)
    {
        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<OrderAggregate>(streamId, new OrderCreated(number, "Alice"));
        await session.SaveChangesAsync();
        return streamId;
    }

    [Fact]
    public async Task a_second_stream_cannot_claim_a_key_in_use()
    {
        StoreOptions(opts => opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline));

        var number = new OrderNumber("ORD-UNIQUE");
        var original = await startOrder(number);
        var claimant = Guid.NewGuid();

        await using var session = theStore.LightweightSession();

        #region sample_natural_key_duplicate_claim

        session.Events.StartStream<OrderAggregate>(claimant, new OrderCreated(number, "Bob"));

        try
        {
            await session.SaveChangesAsync();
        }
        catch (DuplicateNaturalKeyException e)
        {
            // Nothing in the session was saved. The exception says which key was claimed twice,
            // and by which streams
            Console.WriteLine($"{e.Key} already belongs to stream {e.ExistingStreamId}, " +
                              $"so stream {e.ClaimingStreamId} cannot claim it");
        }

        #endregion

        await using var query = theStore.LightweightSession();
        var order = await query.Events.FetchLatest<OrderAggregate, OrderNumber>(number);
        order.ShouldNotBeNull();
        order.Id.ShouldBe(original);
        order.CustomerName.ShouldBe("Alice");
    }

    [Fact]
    public async Task fetch_for_exclusive_writing_by_natural_key()
    {
        StoreOptions(opts => opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline));

        var orderNumber = new OrderNumber("ORD-EXCLUSIVE");
        var streamId = await startOrder(orderNumber);

        await using (var session = theStore.LightweightSession())
        {
            #region sample_fetch_for_exclusive_writing_by_natural_key

            // Locks the stream until the session is saved or disposed
            var stream = await session.Events.FetchForExclusiveWriting<OrderAggregate, OrderNumber>(orderNumber);

            stream.AppendOne(new OrderItemAdded("Gadget", 19.99m));
            await session.SaveChangesAsync();

            #endregion
        }

        await using var query = theStore.LightweightSession();
        var order = await query.Events.FetchLatest<OrderAggregate>(streamId);
        order.ShouldNotBeNull();
        order.TotalAmount.ShouldBe(19.99m);
    }

    [Fact]
    public async Task fetch_latest_by_natural_key_answers_from_the_session()
    {
        StoreOptions(opts =>
        {
            #region sample_natural_key_write_cache_and_identity_map

            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);

            // FetchForWriting by natural key starts from a cached aggregate, as a fetch by stream id does
            opts.Events.CacheAggregatesForWriting<OrderAggregate>();

            // The default. FetchLatest by natural key answers with an aggregate the session already holds
            opts.Events.UseIdentityMapForAggregates = true;

            #endregion
        });

        var number = new OrderNumber("ORD-SESSION");
        var streamId = await startOrder(number);

        await using var session = theStore.IdentitySession();

        #region sample_fetch_latest_by_natural_key_from_the_session

        var stream = await session.Events.FetchForWriting<OrderAggregate, OrderNumber>(number);

        // The aggregate this session already holds, found by its current natural key
        var latest = await session.Events.FetchLatest<OrderAggregate, OrderNumber>(number);

        #endregion

        latest.ShouldBeSameAs(stream.Aggregate);
        latest!.Id.ShouldBe(streamId);
    }

    // #6153 / jasperfx#1287: a null from an inferred source keeps the key
    [Fact]
    public async Task a_source_that_only_sometimes_sets_the_key_keeps_it()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Shipment>(SnapshotLifecycle.Inline));

        var code = new ShipmentCode("SHP-1");
        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Shipment>(streamId, new ShipmentBooked(code));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new ShipmentUpdated(null, "Left the depot"));
            await session.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession();
        (await query.Events.FetchLatest<Shipment, ShipmentCode>(code))!.Id.ShouldBe(streamId);

        await using var writing = theStore.LightweightSession();
        var stream = await writing.Events.FetchForWriting<Shipment, ShipmentCode>(code);
        stream.Id.ShouldBe(streamId);
        stream.CurrentVersion.ShouldBe(2);
    }

    // #6153 / jasperfx#1287: a null from an explicit source removes the key
    [Fact]
    public async Task a_static_source_returning_null_removes_the_key()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Shipment>(SnapshotLifecycle.Inline));

        var code = new ShipmentCode("SHP-2");
        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Shipment>(streamId, new ShipmentBooked(code));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new ShipmentCodeWithdrawn());
            await session.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession();
        (await query.Events.FetchLatest<Shipment, ShipmentCode>(code)).ShouldBeNull();
    }

    [Fact]
    public async Task natural_key_mappings_registered_on_the_projection()
    {
        StoreOptions(opts =>
        {
            #region sample_natural_key_explicit_registration

            opts.Projections.Snapshot<RegisteredOrder>(SnapshotLifecycle.Inline, p =>
                ((SingleStreamProjection<RegisteredOrder, Guid>)p).NaturalKeyFor(x => x
                    // Read the key from the event body
                    .SetBy<OrderCreated>(e => e.OrderNumber)
                    // ...or from the whole event, when the key depends on its metadata, such as the
                    // stream key, timestamp or headers
                    .SetByEvent<OrderNumberChanged>(e => e.Data.NewOrderNumber)));

            #endregion
        });

        await renumberAndFetch();
    }

    [Fact]
    public async Task natural_key_mappings_registered_in_a_projection_class()
    {
        StoreOptions(opts => opts.Projections.Add<RegisteredOrderProjection>(ProjectionLifecycle.Inline));

        await renumberAndFetch();
    }

    private async Task renumberAndFetch()
    {
        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<RegisteredOrder>(streamId, new OrderCreated(new OrderNumber("ORD-OLD"), "Alice"),
                new OrderNumberChanged(new OrderNumber("ORD-NEW")));
            await session.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession();
        (await query.Events.FetchLatest<RegisteredOrder, OrderNumber>(new OrderNumber("ORD-NEW")))!.Id
            .ShouldBe(streamId);
        (await query.Events.FetchLatest<RegisteredOrder, OrderNumber>(new OrderNumber("ORD-OLD"))).ShouldBeNull();
    }
}
