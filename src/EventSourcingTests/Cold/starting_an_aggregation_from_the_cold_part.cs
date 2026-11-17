#pragma warning disable MARTEN001 // cold event storage is experimental
using System;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Cold;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Cold;

/// <summary>
///     An application that moves a stream's events to cold storage takes the stream's projected documents along.
///     When the stream then gets a new event, its single-stream aggregations start from the cold part.
/// </summary>
public class starting_an_aggregation_from_the_cold_part: OneOffConfigurationsContext
{
    private readonly InMemoryColdEventStore _cold = new();

    private void configure(ProjectionLifecycle lifecycle)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            opts.Projections.Add(new EvolvedCartProjection(), lifecycle);
        });
    }

    private async Task<string> startCart()
    {
        var key = Guid.NewGuid().ToString();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream(key, new CartOpened("anne"), new ItemAdded("apple"));
        await session.SaveChangesAsync();
        return key;
    }

    /// <summary>Moves the stream's events through version 2 and, unless told otherwise, its projected document.</summary>
    private async Task moveToCold(string key, bool withDocument = true)
    {
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);
        if (!withDocument) return;

        await using var session = theStore.LightweightSession();
        session.HardDelete<EvolvedCart>(key);
        await session.SaveChangesAsync();
    }

    private async Task append(string key, params object[] events)
    {
        await using var session = theStore.LightweightSession();
        session.Events.Append(key, events);
        await session.SaveChangesAsync();
    }

    private async Task<EvolvedCart> load(string key)
    {
        await using var session = theStore.QuerySession();
        var cart = await session.LoadAsync<EvolvedCart>(key);
        cart.ShouldNotBeNull();
        return cart;
    }

    private void freezeDocument(string key) =>
        _cold.Snapshots[(StorageConstants.DefaultTenantId, key)] =
            new EvolvedCart { Id = key, Customer = "frozen", Items = ["fig"] };

    [Fact]
    public async Task an_inline_aggregation_folds_the_cold_events_when_its_document_is_gone()
    {
        configure(ProjectionLifecycle.Inline);
        var key = await startCart();
        await moveToCold(key);

        await append(key, new ItemAdded("pear"));

        var cart = await load(key);
        cart.Customer.ShouldBe("anne");
        cart.Items.ShouldBe(["apple", "pear"]);
        _cold.SnapshotLoads.ShouldBe(1);
    }

    [Fact]
    public async Task an_inline_aggregation_starts_from_the_document_the_cold_store_kept()
    {
        configure(ProjectionLifecycle.Inline);
        var key = await startCart();
        await moveToCold(key);
        freezeDocument(key);

        await append(key, new ItemAdded("pear"));

        var cart = await load(key);
        cart.Customer.ShouldBe("frozen");
        cart.Items.ShouldBe(["fig", "pear"]);
        _cold.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task an_inline_aggregation_whose_document_is_still_there_does_not_call_the_cold_store()
    {
        configure(ProjectionLifecycle.Inline);
        var key = await startCart();
        await moveToCold(key, withDocument: false);

        await append(key, new ItemAdded("pear"));

        (await load(key)).Items.ShouldBe(["apple", "pear"]);
        _cold.Reads.ShouldBe(0);
        _cold.SnapshotLoads.ShouldBe(0);
    }

    [Fact]
    public async Task a_document_gone_after_warm_events_were_projected_is_not_started_from_the_cold_part()
    {
        configure(ProjectionLifecycle.Inline);
        var key = await startCart();
        await moveToCold(key);
        await append(key, new ItemAdded("pear"));
        var reads = _cold.Reads;

        // Version 3 is warm and was projected, so a missing document is the projection's outcome, not the move's
        await using (var session = theStore.LightweightSession())
        {
            session.HardDelete<EvolvedCart>(key);
            await session.SaveChangesAsync();
        }

        await append(key, new ItemAdded("plum"));

        (await load(key)).Items.ShouldBe(["plum"]);
        _cold.Reads.ShouldBe(reads);
        _cold.SnapshotLoads.ShouldBe(1);
    }

    [Fact]
    public async Task an_async_aggregation_folds_the_cold_events_when_its_document_is_gone()
    {
        configure(ProjectionLifecycle.Async);
        using var daemon = await startDaemon();
        var key = await startCart();
        await daemon.WaitForNonStaleData(30.Seconds());
        await moveToCold(key);

        await append(key, new ItemAdded("pear"), new ItemAdded("plum"));
        await daemon.WaitForNonStaleData(30.Seconds());

        var cart = await load(key);
        cart.Customer.ShouldBe("anne");
        cart.Items.ShouldBe(["apple", "pear", "plum"]);
        _cold.SnapshotLoads.ShouldBe(1);
    }

    [Fact]
    public async Task an_async_aggregation_starts_from_the_document_the_cold_store_kept()
    {
        configure(ProjectionLifecycle.Async);
        using var daemon = await startDaemon();
        var key = await startCart();
        await daemon.WaitForNonStaleData(30.Seconds());
        await moveToCold(key);
        freezeDocument(key);

        await append(key, new ItemAdded("pear"));
        await daemon.WaitForNonStaleData(30.Seconds());

        var cart = await load(key);
        cart.Customer.ShouldBe("frozen");
        cart.Items.ShouldBe(["fig", "pear"]);
        _cold.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task an_async_aggregation_whose_document_is_still_there_does_not_call_the_cold_store()
    {
        configure(ProjectionLifecycle.Async);
        using var daemon = await startDaemon();
        var key = await startCart();
        await daemon.WaitForNonStaleData(30.Seconds());
        await moveToCold(key, withDocument: false);

        await append(key, new ItemAdded("pear"));
        await daemon.WaitForNonStaleData(30.Seconds());

        (await load(key)).Items.ShouldBe(["apple", "pear"]);
        _cold.Reads.ShouldBe(0);
        _cold.SnapshotLoads.ShouldBe(0);
    }

    [Fact]
    public async Task an_aggregation_that_ignores_cold_streams_folds_the_warm_events_only()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            var projection = new EvolvedCartProjection();
            projection.Options.IgnoreColdStreams();
            opts.Projections.Add(projection, ProjectionLifecycle.Inline);
        });
        var key = await startCart();
        await moveToCold(key);

        await append(key, new ItemAdded("pear"));

        var cart = await load(key);
        cart.Customer.ShouldBeNull();
        cart.Items.ShouldBe(["pear"]);
        _cold.Reads.ShouldBe(0);
        _cold.SnapshotLoads.ShouldBe(0);
    }

    private void configureComposite(bool compositeIgnores, bool memberIgnores)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            opts.Projections.CompositeProjectionFor("Carts", composite =>
            {
                var member = new EvolvedCartProjection();
                if (memberIgnores) member.Options.IgnoreColdStreams();
                composite.Add(member);
                if (compositeIgnores) composite.Options.IgnoreColdStreams();
            });
        });
    }

    private async Task<EvolvedCart> appendToColdCartThroughTheDaemon()
    {
        using var daemon = await startDaemon();
        var key = await startCart();
        await daemon.WaitForNonStaleData(30.Seconds());
        await moveToCold(key);

        await append(key, new ItemAdded("pear"));
        await daemon.WaitForNonStaleData(30.Seconds());

        return await load(key);
    }

    [Fact]
    public async Task a_member_of_a_composite_that_ignores_cold_streams_folds_the_warm_events_only()
    {
        configureComposite(compositeIgnores: true, memberIgnores: false);

        var cart = await appendToColdCartThroughTheDaemon();

        cart.Customer.ShouldBeNull();
        cart.Items.ShouldBe(["pear"]);
        _cold.Reads.ShouldBe(0);
        _cold.SnapshotLoads.ShouldBe(0);
    }

    [Fact]
    public async Task a_composite_member_that_alone_ignores_cold_streams_still_starts_from_the_cold_part()
    {
        configureComposite(compositeIgnores: false, memberIgnores: true);

        var cart = await appendToColdCartThroughTheDaemon();

        cart.Customer.ShouldBe("anne");
        cart.Items.ShouldBe(["apple", "pear"]);
        _cold.SnapshotLoads.ShouldBe(1);
    }

    private async Task<IProjectionDaemon> startDaemon()
    {
        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        return daemon;
    }
}
