using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

#region sample_nullable_natural_key_aggregate

public readonly record struct Sku(string Value);

public record ProductListed(Sku Sku, string Name);

public record ProductSkuChanged(Sku? NewSku);

public record ProductRestocked(int Quantity);

// The key is a struct, declared nullable on the aggregate, and set by sources returning both Sku and Sku?
public class Product
{
    public Guid Id { get; set; }

    [NaturalKey]
    public Sku? Sku { get; set; }

    public string Name { get; set; } = string.Empty;
    public int Stock { get; set; }

    [NaturalKeySource]
    public static Sku KeyFor(ProductListed e) => e.Sku;

    [NaturalKeySource]
    public static Sku? KeyFor(ProductSkuChanged e) => e.NewSku;

    public void Apply(ProductListed e)
    {
        Sku = e.Sku;
        Name = e.Name;
    }

    public void Apply(ProductSkuChanged e) => Sku = e.NewSku;

    public void Apply(ProductRestocked e) => Stock += e.Quantity;
}

#endregion

// An async view mapped to the owner's key, declared non-nullable where the owner's is nullable
public class ProductStock
{
    public Guid Id { get; set; }

    [MappedNaturalKey<Product>]
    public Sku Sku { get; set; }

    public int Stock { get; set; }

    public static ProductStock Create(ProductListed e) => new() { Sku = e.Sku };

    public void Apply(ProductRestocked e) => Stock += e.Quantity;
}

public record TicketOpened(int? Number);

public record TicketNoted(string Note);

public class Ticket
{
    public Guid Id { get; set; }

    [NaturalKey]
    public int? Number { get; set; }

    public int Notes { get; set; }

    [NaturalKeySource]
    public void Apply(TicketOpened e) => Number = e.Number;

    public void Apply(TicketNoted e) => Notes++;
}

public class nullable_natural_keys: OneOffConfigurationsContext
{
    [Fact]
    public async Task fetching_by_the_nullable_key_type_is_refused()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Product>(SnapshotLifecycle.Inline));

        var sku = new Sku("SKU-2");
        theSession.Events.StartStream<Product>(Guid.NewGuid(), new ProductListed(sku, "Gadget"));
        await theSession.SaveChangesAsync();

        // The caller knows whether it has a key, so it handles null itself and fetches by Sku
        Sku? asNullable = sku;

        var ex = await Should.ThrowAsync<ArgumentException>(async () =>
            await theSession.Events.FetchLatest<Product, Sku?>(asNullable));
        ex.Message.ShouldContain("Fetch by");

        await Should.ThrowAsync<ArgumentException>(() =>
            theSession.Events.FetchForWriting<Product, Sku?>(asNullable));
        await Should.ThrowAsync<ArgumentException>(() =>
            theSession.Events.FetchForWriting<Product, Sku?>(asNullable, 1));
        await Should.ThrowAsync<ArgumentException>(() =>
            theSession.Events.FetchForExclusiveWriting<Product, Sku?>(asNullable));
        await Should.ThrowAsync<ArgumentException>(async () =>
            await theSession.Events.FetchLatest<Ticket, int?>(1042));

        var batch = theSession.CreateBatchQuery();
        Should.Throw<ArgumentException>(() => batch.Events.FetchForWriting<Product, Sku?>(asNullable));
    }

    [Fact]
    public async Task a_null_key_is_refused_for_writing_and_finds_nothing_for_reading()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<Product>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
        });

        // As an empty Guid or a null string stream id is: no stream to load, and no key for a new one to claim.
        // default(Sku) is a key whose value is null, which is what a missing value binds to.
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            theSession.Events.FetchForWriting<Product, Sku>(default));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            theSession.Events.FetchForWriting<Product, Sku>(default, 1));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            theSession.Events.FetchForExclusiveWriting<Product, Sku>(default));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            theSession.Events.FetchForWriting<OrderAggregate, OrderNumber>(null!));

        var batch = theSession.CreateBatchQuery();
        Should.Throw<ArgumentOutOfRangeException>(() => batch.Events.FetchForWriting<Product, Sku>(default));

        // A read finds nothing, without a query
        (await theSession.Events.FetchLatest<Product, Sku>(default)).ShouldBeNull();
        (await theSession.Events.FetchLatest<OrderAggregate, OrderNumber>(null!)).ShouldBeNull();

        var reads = theSession.CreateBatchQuery();
        var latest = reads.Events.FetchLatest<Product, Sku>(default);
        await reads.Execute();
        (await latest).ShouldBeNull();
    }

    [Fact]
    public async Task a_source_returning_the_nullable_key_renames_the_stream()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Product>(SnapshotLifecycle.Inline));

        var original = new Sku("SKU-OLD");
        var renamed = new Sku("SKU-NEW");
        theSession.Events.StartStream<Product>(Guid.NewGuid(), new ProductListed(original, "Doohickey"));
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<Product, Sku>(original);
        stream.AppendOne(new ProductSkuChanged(renamed));
        await theSession.SaveChangesAsync();

        (await theSession.Events.FetchLatest<Product, Sku>(original)).ShouldBeNull();
        (await theSession.Events.FetchLatest<Product, Sku>(renamed))!.Sku.ShouldBe(renamed);
    }

    [Fact]
    public async Task setting_the_key_back_to_null_retires_it()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Product>(SnapshotLifecycle.Inline));

        var sku = new Sku("SKU-CLEARED");
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<Product>(streamId, new ProductListed(sku, "Sprocket"));
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new ProductSkuChanged(null));
        await theSession.SaveChangesAsync();

        (await theSession.Events.FetchLatest<Product, Sku>(sku)).ShouldBeNull();
        (await lookupRowsFor(streamId)).ShouldBe(0);

        // The stream can take a key again
        var next = new Sku("SKU-AGAIN");
        theSession.Events.Append(streamId, new ProductSkuChanged(next));
        await theSession.SaveChangesAsync();

        (await theSession.Events.FetchLatest<Product, Sku>(next))!.Id.ShouldBe(streamId);
    }

    [Fact]
    public async Task a_retired_key_can_be_claimed_by_another_stream()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Product>(SnapshotLifecycle.Inline));

        var sku = new Sku("SKU-FREED");
        var first = Guid.NewGuid();
        theSession.Events.StartStream<Product>(first, new ProductListed(sku, "First"));
        await theSession.SaveChangesAsync();

        theSession.Events.Append(first, new ProductSkuChanged(null));
        await theSession.SaveChangesAsync();

        var second = Guid.NewGuid();
        theSession.Events.StartStream<Product>(second, new ProductListed(sku, "Second"));
        await theSession.SaveChangesAsync();

        (await theSession.Events.FetchLatest<Product, Sku>(sku))!.Id.ShouldBe(second);
    }

    [Fact]
    public async Task a_rebuild_leaves_a_cleared_key_retired()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Product>(SnapshotLifecycle.Inline));
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var sku = new Sku("SKU-REBUILT");
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<Product>(streamId, new ProductListed(sku, "Cog"), new ProductSkuChanged(null));
        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<Product>(CancellationToken.None);

        (await lookupRowsFor(streamId)).ShouldBe(0);
        (await theSession.Events.FetchLatest<Product, Sku>(sku)).ShouldBeNull();
    }

    private async Task<long> lookupRowsFor(Guid streamId)
    {
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"select count(*) from {theStore.Options.Events.DatabaseSchemaName}.mt_natural_key_product where stream_id = '{streamId}'";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task a_nullable_struct_key_is_auto_registered_on_first_fetch()
    {
        // No projection registered: the first FetchForWriting by Sku registers an Inline snapshot
        StoreOptions(_ => { });

        var sku = new Sku("SKU-AUTO");
        var stream = await theSession.Events.FetchForWriting<Product, Sku>(sku);
        stream.Aggregate.ShouldBeNull();
        stream.AppendOne(new ProductListed(sku, "Thingamajig"));
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        (await session.Events.FetchLatest<Product, Sku>(sku))!.Name.ShouldBe("Thingamajig");
    }

    [Fact]
    public async Task a_non_nullable_mapped_key_reads_a_nullable_owned_key()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<Product>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<ProductStock>(SnapshotLifecycle.Async);
        });

        var sku = new Sku("SKU-MAPPED");
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<Product>(streamId, new ProductListed(sku, "Gizmo"), new ProductRestocked(3));
        await theSession.SaveChangesAsync();

        var stock = await theSession.Events.FetchLatest<ProductStock, Sku>(sku);
        stock.ShouldNotBeNull();
        stock.Id.ShouldBe(streamId);
        stock.Stock.ShouldBe(3);
    }

    [Fact]
    public async Task a_nullable_primitive_key_is_stored_and_fetched_as_its_primitive()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Ticket>(SnapshotLifecycle.Inline));

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<Ticket>(streamId, new TicketOpened(1042), new TicketNoted("first"));

        // A ticket opened without a number has no key, and so no lookup row
        theSession.Events.StartStream<Ticket>(Guid.NewGuid(), new TicketOpened(null));
        await theSession.SaveChangesAsync();

        var latest = await theSession.Events.FetchLatest<Ticket, int>(1042);
        latest.ShouldNotBeNull();
        latest.Id.ShouldBe(streamId);
        latest.Notes.ShouldBe(1);
    }
}
