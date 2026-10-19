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

    private async Task appendItems(object id, params string[] names)
    {
        await using var session = theStore.LightweightSession();
        if (id is Guid guid)
        {
            var stream = await session.Events.FetchForWriting<GuidCart>(guid);
            stream.AppendMany(names.Select(x => (object)new ItemAdded(x)).ToArray());
        }
        else
        {
            var stream = await session.Events.FetchForWriting<Cart>((string)id);
            stream.AppendMany(names.Select(x => (object)new ItemAdded(x)).ToArray());
        }

        await session.SaveChangesAsync();
    }

    [Theory]
    [InlineData(StreamIdentity.AsString)]
    [InlineData(StreamIdentity.AsGuid)]
    public async Task a_stream_goes_cold_again_and_again_as_it_grows(StreamIdentity identity)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = identity;
            opts.Events.UseColdEventStore(_cold);
        });

        object id = identity == StreamIdentity.AsGuid ? Guid.NewGuid() : Guid.NewGuid().ToString();
        await using (var session = theStore.LightweightSession())
        {
            object[] events = [new CartOpened("anne"), new ItemAdded("apple"), new ItemAdded("pear")];
            if (id is Guid guid) session.Events.StartStream<GuidCart>(guid, events);
            else session.Events.StartStream<Cart>((string)id, events);
            await session.SaveChangesAsync();
        }

        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, id, 3);
        await appendItems(id, "plum", "fig");
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, id, 5);
        await appendItems(id, "kiwi");

        _cold.StoredVersions(StorageConstants.DefaultTenantId, id).ShouldBe([1L, 2L, 3L, 4L, 5L]);

        await using var first = theStore.LightweightSession();
        await using var second = theStore.LightweightSession();

        while (_cold.ReadThrough.TryDequeue(out _)) { }

        if (id is Guid g)
        {
            var stream = await first.Events.FetchForWriting<GuidCart>(g);
            stream.Aggregate!.Items.ShouldBe(["apple", "pear", "plum", "fig", "kiwi"]);
            stream.StartingVersion.ShouldBe(6);
            stream.AppendOne(new ItemAdded("lime"));

            (await second.Events.FetchForWriting<GuidCart>(g)).AppendOne(new ItemAdded("date"));
        }
        else
        {
            var stream = await first.Events.FetchForWriting<Cart>((string)id);
            stream.Aggregate!.Items.ShouldBe(["apple", "pear", "plum", "fig", "kiwi"]);
            stream.StartingVersion.ShouldBe(6);
            stream.AppendOne(new ItemAdded("lime"));

            (await second.Events.FetchForWriting<Cart>((string)id)).AppendOne(new ItemAdded("date"));
        }

        _cold.ReadThrough.ShouldBe([5L, 5L]);

        await first.SaveChangesAsync();
        await Should.ThrowAsync<ConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task marking_at_or_below_the_boundary_again_changes_nothing()
    {
        useStringStreams();
        var key = await startCart(new ItemAdded("apple"), new ItemAdded("pear"), new ItemAdded("plum"));
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 3);

        await using (var session = theStore.LightweightSession())
        {
            // A retried move, and a stale one
            session.Events.MarkStreamCold(key, 3);
            session.Events.MarkStreamCold(key, 2);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.Events.FetchStreamAsync(key)).Select(x => x.Version).ShouldBe([4L]);

            while (_cold.ReadThrough.TryDequeue(out _)) { }
            (await session.Events.FetchLatest<Cart>(key))!.Items.ShouldBe(["apple", "pear", "plum"]);
            _cold.ReadThrough.ShouldBe([3L]);
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
