using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Cold;

public class CartItemCount
{
    public string Id { get; set; } = null!;
    public int Count { get; set; }
}

public partial class CartItemCountProjection: SingleStreamProjection<CartItemCount, string>
{
    public override CartItemCount Evolve(CartItemCount snapshot, string id, IEvent e)
    {
        snapshot ??= new CartItemCount { Id = id };
        if (e.Data is ItemAdded) snapshot.Count++;
        return snapshot;
    }
}

public class marking_cold_waits_for_async_shards: OneOffConfigurationsContext
{
    private readonly InMemoryColdEventStore _cold = new();

    private async Task<string> startCart()
    {
        var key = Guid.NewGuid().ToString();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<Cart>(key, new CartOpened("anne"), new ItemAdded("apple"));
        await session.SaveChangesAsync();
        return key;
    }

    [Fact]
    public async Task an_async_shard_that_has_not_processed_the_events_refuses_the_move_until_it_caught_up()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            opts.Projections.Add(new CartItemCountProjection(), ProjectionLifecycle.Async);
        });

        var key = await startCart();

        var e = await Should.ThrowAsync<InvalidOperationException>(
            () => theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2));
        e.Message.ShouldContain(nameof(CartItemCount));
        (await theStore.LightweightSession().Events.FetchStreamAsync(key)).Count.ShouldBe(2);

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(30.Seconds());
        }

        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);
        (await theStore.LightweightSession().Events.FetchStreamAsync(key)).ShouldBeEmpty();
    }

    [Fact]
    public async Task inline_projections_do_not_hold_the_move_back()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            opts.Projections.Add(new CartItemCountProjection(), ProjectionLifecycle.Inline);
        });

        var key = await startCart();
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);
        (await theStore.LightweightSession().Events.FetchStreamAsync(key)).ShouldBeEmpty();
    }
}
