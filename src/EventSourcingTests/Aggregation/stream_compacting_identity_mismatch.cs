using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

/// <summary>
/// Follow-up to #5240. Compacting is identity-sensitive — <c>StreamCompactingRequest</c> carries one
/// of StreamId/StreamKey and <c>ExecuteAsync</c> branches on the store's configured
/// <see cref="StreamIdentity" /> rather than on which overload was called — but unlike every other
/// identity-sensitive entry point (appends, FetchForWriting) it never asserted the two agreed.
/// </summary>
/// <remarks>
/// The two failure modes were not symmetric, which is why both directions are pinned here rather
/// than just the one that throws:
/// <list type="bullet">
/// <item>string overload on a Guid store: <c>InvalidOperationException("Nullable object must have a
/// value")</c> out of <c>request.StreamId!.Value</c> — an error naming nothing actionable.</item>
/// <item>Guid overload on a string store: the AsString branch read a null StreamKey, matched no
/// stream, and returned at <c>if (!events.Any()) return;</c> — compaction <em>silently did
/// nothing</em>, which is the worse of the two.</item>
/// </list>
/// </remarks>
public class stream_compacting_identity_mismatch: OneOffConfigurationsContext
{
    public AEvent A() => new();
    public BEvent B() => new();
    public CEvent C() => new();

    [Fact]
    public async Task session_level_string_overload_against_guid_store_is_rejected()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Letters>(SnapshotLifecycle.Inline));

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<Letters>(streamId, A(), B(), C());
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var session = theStore.LightweightSession();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => session.Events.CompactStreamAsync<Letters>(streamId.ToString()));

        ex.Message.ShouldBe("This Marten event store is configured to identify streams with Guids");
    }

    [Fact]
    public async Task session_level_guid_overload_against_string_store_is_rejected()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Add<LetterCountsByStringProjection>(ProjectionLifecycle.Inline);
        });

        var streamKey = Guid.NewGuid().ToString();
        theSession.Events.StartStream<LetterCountsByString>(streamKey, A(), B(), C());
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var session = theStore.LightweightSession();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => session.Events.CompactStreamAsync<LetterCountsByString>(Guid.NewGuid()));

        ex.Message.ShouldBe("This Marten event store is configured to identify streams with strings");
    }

    /// <summary>
    /// The silent branch, pinned explicitly: the stream must still hold all three of its original
    /// events. Pre-fix this call returned successfully having compacted nothing, so a caller had no
    /// way to tell a no-op from a completed compaction.
    /// </summary>
    [Fact]
    public async Task the_silently_ignored_mirror_case_leaves_the_stream_untouched_and_now_throws()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Add<LetterCountsByStringProjection>(ProjectionLifecycle.Inline);
        });

        var streamKey = Guid.NewGuid().ToString();
        theSession.Events.StartStream<LetterCountsByString>(streamKey, A(), B(), C());
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var session = theStore.LightweightSession();

        await Should.ThrowAsync<InvalidOperationException>(
            () => session.Events.CompactStreamAsync<LetterCountsByString>(Guid.NewGuid()));

        var events = await session.Events.FetchStreamAsync(streamKey, token: TestContext.Current.CancellationToken);
        events.Count.ShouldBe(3);
        events.Any(x => x.Data is Compacted<LetterCountsByString>).ShouldBeFalse();
    }

    [Fact]
    public async Task store_level_string_overload_against_guid_store_names_the_real_mistake()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Letters>(SnapshotLifecycle.Inline));

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<Letters>(streamId, A(), B(), C());
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Pre-fix this reported "stream not found or no aggregate type associated", which is a
        // misdiagnosis — the stream is right there, just identified the other way.
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => ((IEventStore)theStore).CompactStreamAsync(streamId.ToString(), CancellationToken.None));

        ex.Message.ShouldBe("This Marten event store is configured to identify streams with Guids");
    }

    [Fact]
    public async Task store_level_guid_overload_against_string_store_names_the_real_mistake()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Add<LetterCountsByStringProjection>(ProjectionLifecycle.Inline);
        });

        var streamKey = Guid.NewGuid().ToString();
        theSession.Events.StartStream<LetterCountsByString>(streamKey, A(), B(), C());
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => ((IEventStore)theStore).CompactStreamAsync(Guid.NewGuid(), CancellationToken.None));

        ex.Message.ShouldBe("This Marten event store is configured to identify streams with strings");
    }

    /// <summary>
    /// The coverage gap called out while reviewing #5240: that PR started propagating the caller's
    /// CancellationToken into the request (the old code passed a literal null for `configure`, so the
    /// token was accepted and discarded), but nothing asserted the request actually honors it. Driven
    /// at the session level because that is where the token is settable, and it is the same
    /// StreamCompactingRequest.CancellationToken the store-level overload now sets.
    /// </summary>
    [Fact]
    public async Task the_requests_cancellation_token_is_honored()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Letters>(SnapshotLifecycle.Inline));

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<Letters>(streamId, A(), B(), C());
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await using var session = theStore.LightweightSession();

        await Should.ThrowAsync<OperationCanceledException>(
            () => session.Events.CompactStreamAsync<Letters>(streamId, x => x.CancellationToken = cancelled.Token));
    }
}
