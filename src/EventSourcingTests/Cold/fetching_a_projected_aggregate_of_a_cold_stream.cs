#pragma warning disable MARTEN001 // cold event storage is experimental
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
///     An application that moves a stream's events to cold storage takes the stream's projected document along. A
///     fetch of an inline or async aggregate then finds no stored document, and starts from the cold part the way
///     the projection itself does.
/// </summary>
public class fetching_a_projected_aggregate_of_a_cold_stream: OneOffConfigurationsContext
{
    private readonly InMemoryColdEventStore _cold = new();

    public static IEnumerable<object[]> Fetches()
    {
        foreach (var lifecycle in new[] { ProjectionLifecycle.Inline, ProjectionLifecycle.Async })
        {
            foreach (var fetch in Enum.GetValues<Fetch>())
            {
                yield return [lifecycle, fetch];
            }
        }
    }

    public enum Fetch
    {
        ForWriting,
        ForWritingAtVersion,
        ForExclusiveWriting,
        Latest,
        LatestJson,
        BatchedForWriting,
        BatchedForWritingAtVersion,
        BatchedLatest
    }

    private void configure(ProjectionLifecycle lifecycle, bool ignoresColdStreams = false)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            var projection = new EvolvedCartProjection();
            if (ignoresColdStreams) projection.Options.IgnoreColdStreams();
            opts.Projections.Add(projection, lifecycle);
        });
    }

    /// <summary>
    ///     A cart whose versions 1 and 2 are cold and, unless told otherwise, whose stored document went with them.
    ///     The async daemon is stopped afterwards, so nothing projects what the test appends.
    /// </summary>
    private async Task<string> coldCart(ProjectionLifecycle lifecycle, bool keepDocument = false)
    {
        var key = Guid.NewGuid().ToString();

        IProjectionDaemon? daemon = null;
        if (lifecycle == ProjectionLifecycle.Async)
        {
            daemon = await theStore.BuildProjectionDaemonAsync();
            await daemon.StartAllAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(key, new CartOpened("anne"), new ItemAdded("apple"));
            await session.SaveChangesAsync();
        }

        if (daemon != null) await daemon.WaitForNonStaleData(30.Seconds());

        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);

        if (daemon != null)
        {
            await daemon.StopAllAsync();
            daemon.Dispose();
        }

        if (!keepDocument)
        {
            await using var session = theStore.LightweightSession();
            session.HardDelete<EvolvedCart>(key);
            await session.SaveChangesAsync();
        }

        return key;
    }

    private void freezeDocument(string key) =>
        _cold.Snapshots[(StorageConstants.DefaultTenantId, key)] =
            new EvolvedCart { Id = key, Customer = "frozen", Items = ["fig"] };

    private async Task<(EvolvedCart? Aggregate, long? Version)> fetch(string key, Fetch how, long expectedVersion = 2)
    {
        await using var session = theStore.LightweightSession();

        switch (how)
        {
            case Fetch.ForWriting:
                return of(await session.Events.FetchForWriting<EvolvedCart>(key));
            case Fetch.ForWritingAtVersion:
                return of(await session.Events.FetchForWriting<EvolvedCart>(key, expectedVersion));
            case Fetch.ForExclusiveWriting:
                return of(await session.Events.FetchForExclusiveWriting<EvolvedCart>(key));
            case Fetch.Latest:
                return (await session.Events.FetchLatest<EvolvedCart>(key), null);
            case Fetch.LatestJson:
                var json = new MemoryStream();
                if (!await session.Events.StreamLatestJson<EvolvedCart>(key, json)) return (null, null);
                json.Position = 0;
                return (theStore.Options.Serializer().FromJson<EvolvedCart>(json), null);
        }

        var batch = session.CreateBatchQuery();
        switch (how)
        {
            case Fetch.BatchedForWriting:
            {
                var stream = batch.Events.FetchForWriting<EvolvedCart>(key);
                await batch.Execute();
                return of(await stream);
            }
            case Fetch.BatchedForWritingAtVersion:
            {
                var stream = batch.Events.FetchForWriting<EvolvedCart>(key, expectedVersion);
                await batch.Execute();
                return of(await stream);
            }
            default:
            {
                var latest = batch.Events.FetchLatest<EvolvedCart>(key);
                await batch.Execute();
                return (await latest, null);
            }
        }

        static (EvolvedCart?, long?) of(IEventStream<EvolvedCart> stream) => (stream.Aggregate, stream.StartingVersion);
    }

    [Theory]
    [MemberData(nameof(Fetches))]
    public async Task a_fetch_folds_the_cold_events_when_the_document_went_with_them(ProjectionLifecycle lifecycle,
        Fetch how)
    {
        configure(lifecycle);
        var key = await coldCart(lifecycle);

        var (cart, version) = await fetch(key, how);

        cart.ShouldNotBeNull();
        cart.Id.ShouldBe(key);
        cart.Customer.ShouldBe("anne");
        cart.Items.ShouldBe(["apple"]);
        version?.ShouldBe(2);
        _cold.SnapshotLoads.ShouldBe(1);
        _cold.Reads.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Fetches))]
    public async Task a_fetch_starts_from_the_document_the_cold_store_kept(ProjectionLifecycle lifecycle, Fetch how)
    {
        configure(lifecycle);
        var key = await coldCart(lifecycle);
        freezeDocument(key);

        var (cart, _) = await fetch(key, how);

        cart.ShouldNotBeNull();
        cart.Customer.ShouldBe("frozen");
        cart.Items.ShouldBe(["fig"]);
        _cold.Reads.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Fetches))]
    public async Task a_fetch_whose_document_is_still_there_does_not_call_the_cold_store(
        ProjectionLifecycle lifecycle, Fetch how)
    {
        configure(lifecycle);
        var key = await coldCart(lifecycle, keepDocument: true);

        var (cart, _) = await fetch(key, how);

        cart.ShouldNotBeNull();
        cart.Items.ShouldBe(["apple"]);
        _cold.Reads.ShouldBe(0);
        _cold.SnapshotLoads.ShouldBe(0);
    }

    [Theory]
    [InlineData(ProjectionLifecycle.Inline)]
    [InlineData(ProjectionLifecycle.Async)]
    public async Task a_fetch_for_a_projection_that_ignores_cold_streams_does_not_call_the_cold_store(
        ProjectionLifecycle lifecycle)
    {
        configure(lifecycle, ignoresColdStreams: true);
        var key = await coldCart(lifecycle);

        (await fetch(key, Fetch.ForWriting)).Aggregate.ShouldBeNull();
        (await fetch(key, Fetch.Latest)).Aggregate.ShouldBeNull();

        _cold.Reads.ShouldBe(0);
        _cold.SnapshotLoads.ShouldBe(0);
    }

    [Theory]
    [InlineData(Fetch.ForWriting)]
    [InlineData(Fetch.ForExclusiveWriting)]
    [InlineData(Fetch.Latest)]
    [InlineData(Fetch.BatchedForWriting)]
    [InlineData(Fetch.BatchedLatest)]
    public async Task an_async_fetch_folds_the_warm_events_the_daemon_has_not_projected_onto_the_cold_part(Fetch how)
    {
        configure(ProjectionLifecycle.Async);
        var key = await coldCart(ProjectionLifecycle.Async);

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(key, new ItemAdded("pear"));
            await session.SaveChangesAsync();
        }

        var (cart, version) = await fetch(key, how);

        cart.ShouldNotBeNull();
        cart.Customer.ShouldBe("anne");
        cart.Items.ShouldBe(["apple", "pear"]);
        version?.ShouldBe(3);
    }

    [Fact]
    public async Task an_inline_fetch_leaves_a_document_the_projection_deleted_after_warm_events_alone()
    {
        configure(ProjectionLifecycle.Inline);
        var key = await coldCart(ProjectionLifecycle.Inline);

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(key, new ItemAdded("pear"));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.HardDelete<EvolvedCart>(key);
            await session.SaveChangesAsync();
        }

        var reads = _cold.Reads;
        var loads = _cold.SnapshotLoads;

        (await fetch(key, Fetch.ForWriting)).Aggregate.ShouldBeNull();
        _cold.Reads.ShouldBe(reads);
        _cold.SnapshotLoads.ShouldBe(loads);
    }

    [Theory]
    [InlineData(ProjectionLifecycle.Inline)]
    [InlineData(ProjectionLifecycle.Async)]
    public async Task appending_after_a_fetch_continues_the_cold_part(ProjectionLifecycle lifecycle)
    {
        configure(lifecycle);
        var key = await coldCart(lifecycle);

        await using (var session = theStore.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<EvolvedCart>(key);
            stream.Aggregate!.Items.ShouldBe(["apple"]);
            stream.AppendOne(new ItemAdded("pear"));
            await session.SaveChangesAsync();
        }

        (await fetch(key, Fetch.Latest)).Aggregate!.Items.ShouldBe(["apple", "pear"]);
    }
}
