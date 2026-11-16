#pragma warning disable MARTEN001 // cold event storage is experimental
using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events.Cold;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Cold;

public class compacting_a_cold_stream: OneOffConfigurationsContext
{
    private readonly InMemoryColdEventStore _cold = new();

    private async Task<string> startCart(params object[] events)
    {
        var key = Guid.NewGuid().ToString();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<Cart>(key, events);
        await session.SaveChangesAsync();
        return key;
    }

    [Fact]
    public async Task is_refused_because_its_snapshot_would_leave_out_the_cold_part()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
        });

        var key = await startCart(new CartOpened("anne"), new ItemAdded("apple"));
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(key, new ItemAdded("pear"), new ItemAdded("plum"));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            var e = await Should.ThrowAsync<InvalidStreamOperationException>(
                () => session.Events.CompactStreamAsync<Cart>(key));
            e.Message.ShouldContain(key);
            e.Message.ShouldContain("cold");

            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.Events.FetchStreamAsync(key)).Select(x => x.Version).ShouldBe([3L, 4L]);
            (await session.Events.FetchLatest<Cart>(key))!.Items.ShouldBe(["apple", "pear", "plum"]);
        }
    }

    [Fact]
    public async Task a_stream_without_a_cold_part_still_compacts()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
        });

        var key = await startCart(new CartOpened("anne"), new ItemAdded("apple"), new ItemAdded("pear"));

        await using (var session = theStore.LightweightSession())
        {
            await session.Events.CompactStreamAsync<Cart>(key);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.Events.FetchStreamAsync(key)).Single().Data.ShouldBeOfType<Compacted<Cart>>();
            (await session.Events.AggregateStreamAsync<Cart>(key))!.Items.ShouldBe(["apple", "pear"]);
        }
    }
}
