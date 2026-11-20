#pragma warning disable MARTEN001 // cold event storage is experimental
using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Cold.Matrix;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Cold;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Cold;

public record ColdTaggedCartId(Guid Value);

public record ColdStrongCartId(string Value);

public class StrongCart
{
    public ColdStrongCartId Id { get; set; } = null!;
    public int Items { get; set; }

    public void Apply(MatrixItemAdded _) => Items++;
}

public class GuidCart
{
    public Guid Id { get; set; }
    public string? Customer { get; set; }

    public void Apply(MatrixOpened e) => Customer = e.Customer;
}

public class GuidNumberedCart
{
    public Guid Id { get; set; }

    [JasperFx.Events.Aggregation.NaturalKey]
    public MatrixOrderNumber Number { get; set; } = null!;

    [JasperFx.Events.Aggregation.NaturalKeySource]
    public void Apply(MatrixOpened e) => Number = e.Number;
}

/// <summary>
///     The combinations a cold event store refuses, each before it can do harm.
/// </summary>
public class what_a_cold_store_refuses: OneOffConfigurationsContext
{
    private readonly InMemoryColdEventStore _cold = new();

    /// <summary>
    ///     Guid stream identity is refused at startup for every lifecycle, by stream id or by natural key: the cold
    ///     store reads streams by their string key.
    /// </summary>
    [Theory]
    [InlineData(ColdLifecycle.Live, false)]
    [InlineData(ColdLifecycle.Inline, false)]
    [InlineData(ColdLifecycle.Async, false)]
    [InlineData(ColdLifecycle.Live, true)]
    [InlineData(ColdLifecycle.Inline, true)]
    [InlineData(ColdLifecycle.Async, true)]
    public void guid_stream_identity_with_a_cold_store(ColdLifecycle lifecycle, bool naturalKey)
    {
        var e = Should.Throw<InvalidOperationException>(() => StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
            opts.Events.UseColdEventStore(_cold);

            if (naturalKey) register<GuidNumberedCart>(opts, lifecycle);
            else register<GuidCart>(opts, lifecycle);
        }));

        e.Message.ShouldContain("string stream identity only");
    }

    private static void register<T>(StoreOptions opts, ColdLifecycle lifecycle) where T : class
    {
        switch (lifecycle)
        {
            case ColdLifecycle.Live:
                opts.Projections.LiveStreamAggregation<T>();
                break;
            case ColdLifecycle.Inline:
                opts.Projections.Snapshot<T>(SnapshotLifecycle.Inline);
                break;
            case ColdLifecycle.Async:
                opts.Projections.Snapshot<T>(SnapshotLifecycle.Async);
                break;
        }
    }

    [Fact]
    public void event_tags_with_a_cold_store()
    {
        // Moving a stream cold deletes its tag rows, so a tag query or a DCB check would pass when it should fail
        var e = Should.Throw<InvalidOperationException>(() => StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            opts.Events.RegisterTagType<ColdTaggedCartId>("cold_cart");
        }));

        e.Message.ShouldContain("Dynamic Consistency Boundary");
        e.Message.ShouldContain(nameof(ColdTaggedCartId));
    }

    [Fact]
    public async Task a_strong_typed_identity_is_not_registered_as_a_tag_type_behind_the_applications_back()
    {
        // Without a cold store, Marten registers a projection's strong-typed identity as a tag type on its own.
        // With one, that would make every such store refuse to start, so it doesn't.
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            opts.Projections.Snapshot<StrongCart>(SnapshotLifecycle.Inline);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        theStore.Options.EventGraph.TagTypes.ShouldBeEmpty();
    }

    private void configureArchiving(bool inlineProjection = false)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            if (inlineProjection) opts.Projections.Snapshot<InlineKeyedCart>(SnapshotLifecycle.Inline);
        });
    }

    private async Task<string> coldStreamAsync(long coldThrough = 2)
    {
        var key = Guid.NewGuid().ToString();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<InlineKeyedCart>(key, new MatrixOpened("anne", new MatrixOrderNumber(key)),
                new MatrixItemAdded("a"), new MatrixItemAdded("b"));
            await session.SaveChangesAsync();
        }

        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, coldThrough);
        return key;
    }

    [Fact]
    public async Task archiving_a_stream_with_a_cold_part()
    {
        // A Live fetch reads a stream's unarchived events, so an archived cold stream would fold its cold part alone
        configureArchiving();
        var key = await coldStreamAsync();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.ArchiveStream(key);
            var e = await Should.ThrowAsync<InvalidStreamOperationException>(() => session.SaveChangesAsync());
            e.Message.ShouldContain(key);
            e.Message.ShouldContain("cold storage");
        }

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamStateAsync(key)).ShouldNotBeNull().IsArchived.ShouldBeFalse();
        (await query.Events.FetchStreamAsync(key)).Select(x => x.Version).ShouldBe([3L]);
    }

    [Fact]
    public async Task archiving_a_cold_stream_with_the_archived_event()
    {
        // The Archived event takes the same route as ArchiveStream, through the inline projection
        configureArchiving(true);
        var key = await coldStreamAsync();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(key, new Archived("done"));
            await Should.ThrowAsync<InvalidStreamOperationException>(() => session.SaveChangesAsync());
        }

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamStateAsync(key)).ShouldNotBeNull().IsArchived.ShouldBeFalse();
    }

    [Fact]
    public async Task archiving_a_stream_without_a_cold_part_still_works()
    {
        configureArchiving();
        var key = Guid.NewGuid().ToString();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<InlineKeyedCart>(key, new MatrixItemAdded("a"));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.ArchiveStream(key);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamStateAsync(key)).ShouldNotBeNull().IsArchived.ShouldBeTrue();
    }

    [Fact]
    public async Task moving_an_archived_stream_cold()
    {
        configureArchiving();
        var key = Guid.NewGuid().ToString();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<InlineKeyedCart>(key, new MatrixItemAdded("a"), new MatrixItemAdded("b"));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.ArchiveStream(key);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.MarkStreamCold(key, 1);
            var e = await Should.ThrowAsync<InvalidOperationException>(() => session.SaveChangesAsync());
            e.Message.ShouldContain("archived");
        }

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamStateAsync(key)).ShouldNotBeNull().ColdVersion.ShouldBeNull();
    }

    [Fact]
    public async Task un_archiving_is_left_alone()
    {
        // A stream can't be cold and archived at once, so un-archiving never meets a cold part
        configureArchiving();
        var key = await coldStreamAsync();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.UnArchiveStream(key);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        (await query.Events.FetchLatest<InlineKeyedCart>(key)).ShouldNotBeNull().Items.ShouldBe(["a", "b"]);
    }
}
