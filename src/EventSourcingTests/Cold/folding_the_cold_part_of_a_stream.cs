using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Cold;

public class folding_the_cold_part_of_a_stream: OneOffConfigurationsContext
{
    private readonly InMemoryColdEventStore _cold = new();

    private void useStringStreams()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
        });
    }

    private async Task<string> startCart(params object[] more)
    {
        var key = Guid.NewGuid().ToString();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<Cart>(key, new object[] { new CartOpened("anne") }.Concat(more).ToArray());
        await session.SaveChangesAsync();
        return key;
    }

    [Fact]
    public async Task a_stream_that_is_entirely_cold_is_fetched_for_writing_whole_and_appends_at_the_next_version()
    {
        useStringStreams();
        var key = await startCart(new ItemAdded("apple"), new ItemAdded("pear"));
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 3);

        await using (var session = theStore.LightweightSession())
        {
            (await session.Events.FetchStreamAsync(key)).ShouldBeEmpty();

            var stream = await session.Events.FetchForWriting<Cart>(key);
            stream.Aggregate.ShouldNotBeNull();
            stream.Aggregate.Customer.ShouldBe("anne");
            stream.Aggregate.Items.ShouldBe(["apple", "pear"]);
            stream.StartingVersion.ShouldBe(3);

            stream.AppendOne(new CartCheckedOut());
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            var warm = await session.Events.FetchStreamAsync(key);
            warm.Single().Version.ShouldBe(4);

            var cart = await session.Events.FetchLatest<Cart>(key);
            cart!.Items.ShouldBe(["apple", "pear"]);
            cart.CheckedOut.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task optimistic_concurrency_still_runs_on_the_stream_version()
    {
        useStringStreams();
        var key = await startCart(new ItemAdded("apple"));
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);

        await using var first = theStore.LightweightSession();
        await using var second = theStore.LightweightSession();

        var one = await first.Events.FetchForWriting<Cart>(key);
        var other = await second.Events.FetchForWriting<Cart>(key);

        one.AppendOne(new ItemAdded("pear"));
        await first.SaveChangesAsync();

        other.AppendOne(new ItemAdded("plum"));
        await Should.ThrowAsync<ConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task a_cold_prefix_is_folded_before_the_warm_events()
    {
        useStringStreams();
        var key = await startCart(new ItemAdded("apple"), new ItemAdded("pear"), new ItemAdded("plum"));
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);

        await using var session = theStore.LightweightSession();
        (await session.Events.FetchStreamAsync(key)).Select(x => x.Version).ShouldBe([3L, 4L]);

        var stream = await session.Events.FetchForWriting<Cart>(key);
        stream.Aggregate!.Items.ShouldBe(["apple", "pear", "plum"]);
        stream.StartingVersion.ShouldBe(4);

        (await session.Events.FetchLatest<Cart>(key))!.Items.ShouldBe(["apple", "pear", "plum"]);
        (await session.Events.AggregateStreamAsync<Cart>(key))!.Items.ShouldBe(["apple", "pear", "plum"]);
        (await session.Events.AggregateStreamAsync<Cart>(key, version: 2))!.Items.ShouldBe(["apple"]);
    }

    [Fact]
    public async Task a_stream_without_a_cold_part_never_asks_the_cold_store()
    {
        useStringStreams();
        var key = await startCart(new ItemAdded("apple"));

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<Cart>(key);
        stream.Aggregate!.Items.ShouldBe(["apple"]);
        (await session.Events.AggregateStreamAsync<Cart>(key))!.Items.ShouldBe(["apple"]);

        _cold.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task exclusive_writing_reads_the_cold_part_inside_its_transaction()
    {
        useStringStreams();
        var key = await startCart(new ItemAdded("apple"));
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForExclusiveWriting<Cart>(key);
        stream.Aggregate!.Items.ShouldBe(["apple"]);
        stream.AppendOne(new ItemAdded("pear"));
        await session.SaveChangesAsync();

        (await theStore.LightweightSession().Events.FetchLatest<Cart>(key))!.Items.ShouldBe(["apple", "pear"]);
    }

    [Fact]
    public async Task batched_fetches_read_the_cold_part_after_the_batch()
    {
        useStringStreams();
        var cold = await startCart(new ItemAdded("apple"));
        var warm = await startCart(new ItemAdded("pear"));
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, cold, 2);

        await using var session = theStore.LightweightSession();
        var streams = await session.Events.FetchManyForWriting<Cart>([cold, warm]);
        streams[0].Aggregate!.Items.ShouldBe(["apple"]);
        streams[0].StartingVersion.ShouldBe(2);
        streams[1].Aggregate!.Items.ShouldBe(["pear"]);

        var batch = session.CreateBatchQuery();
        var latest = batch.Events.FetchLatest<Cart>(cold);
        await batch.Execute();
        (await latest)!.Items.ShouldBe(["apple"]);
    }

    [Fact]
    public async Task a_cold_part_is_marked_in_steps_and_can_be_forgotten_again()
    {
        useStringStreams();
        var key = await startCart(new ItemAdded("apple"), new ItemAdded("pear"));
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 1);
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);

        await using (var session = theStore.LightweightSession())
        {
            (await session.Events.FetchStreamAsync(key)).Select(x => x.Version).ShouldBe([3L]);
            (await session.Events.FetchLatest<Cart>(key))!.Items.ShouldBe(["apple", "pear"]);

            session.Events.MarkStreamWarm(key);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            var reads = _cold.Reads;
            (await session.Events.FetchStreamIncludingColdAsync(key)).Select(x => x.Version).ShouldBe([3L]);
            _cold.Reads.ShouldBe(reads);
        }
    }

    [Fact]
    public async Task marking_beyond_the_end_of_the_stream_fails_the_unit_of_work()
    {
        useStringStreams();
        var key = await startCart(new ItemAdded("apple"));

        await using var session = theStore.LightweightSession();
        session.Events.MarkStreamCold(key, 3);
        await Should.ThrowAsync<Exception>(() => session.SaveChangesAsync());

        (await theStore.LightweightSession().Events.FetchStreamAsync(key)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task guid_identified_streams_fold_their_cold_part()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
            opts.Events.UseColdEventStore(_cold);
        });

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<GuidCart>(id, new CartOpened("anne"), new ItemAdded("apple"));
            await session.SaveChangesAsync();
        }

        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, id, 2);

        await using (var session = theStore.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<GuidCart>(id);
            stream.Aggregate!.Id.ShouldBe(id);
            stream.Aggregate.Items.ShouldBe(["apple"]);

            stream.AppendOne(new ItemAdded("pear"));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.Events.FetchLatest<GuidCart>(id))!.Items.ShouldBe(["apple", "pear"]);
            (await session.Events.FetchStreamIncludingColdAsync(id)).Select(x => x.Version).ShouldBe([1L, 2L, 3L]);
        }
    }

    [Fact]
    public async Task live_projections_get_the_cold_part_whichever_way_they_fold()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            opts.Projections.Add(new EvolvedCartProjection(), ProjectionLifecycle.Live);
            opts.Projections.Add(new AsyncEvolvedCartProjection(), ProjectionLifecycle.Live);
            opts.Projections.Add(new DecidedCartProjection(), ProjectionLifecycle.Live);
        });

        var key = await startCart(new ItemAdded("apple"), new ItemAdded("pear"));
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);

        await using var session = theStore.LightweightSession();

        async Task shouldSeeEverything<T>() where T : CartSummary
        {
            var stream = await session.Events.FetchForWriting<T>(key);
            stream.Aggregate!.Customer.ShouldBe("anne");
            stream.Aggregate.Items.ShouldBe(["apple", "pear"]);
            stream.StartingVersion.ShouldBe(3);
        }

        await shouldSeeEverything<EvolvedCart>();
        await shouldSeeEverything<AsyncEvolvedCart>();
        await shouldSeeEverything<DecidedCart>();
    }
}
