#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

/// <summary>
///     <c>EventStatement</c> -- the query behind <c>FetchStreamAsync</c>, <c>AggregateStreamAsync</c>, the live
///     plan's <c>FetchLatest</c> and the batched <c>FetchStream</c> -- emitted the stream predicate only
///     <c>if (StreamId != Guid.Empty)</c>, and only for a non-empty key. So a stream read of
///     <c>Guid.Empty</c> (or <c>""</c>) read every non-archived event of the tenant, or of the whole store when it
///     is single-tenanted, and <c>CompactStreamAsync</c> then rewrote the newest event in the store -- another
///     stream's -- as <c>Compacted&lt;T&gt;</c> and deleted every other event, in one transaction.
///     <para>
///     Marten refuses both values on <c>Append</c> and <c>StartStream</c>, but not everything goes through those:
///     <c>BulkInsertEventsAsync</c>, a DCB boundary routing on an empty tag value, legacy data and SQL can all
///     leave such a stream behind, and an unset id in calling code reaches the reads with no stream at all.
///     </para>
/// </summary>
public class empty_stream_identity_is_not_a_wildcard: OneOffConfigurationsContext
{
    private readonly Guid _first = Guid.NewGuid();
    private readonly Guid _second = Guid.NewGuid();

    private async Task seedTwoStreams()
    {
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<Letters>(_first, new AEvent(), new BEvent(), new AEvent());
        session.Events.StartStream<Letters>(_second, new AEvent(), new CEvent());
        await session.SaveChangesAsync();
    }

    private async Task seedAnEmptyGuidStream()
    {
        // The way a bypass leaves one behind: StreamAction does not guard the id, and neither does the bulk path
        await theStore.BulkInsertEventsAsync([StreamAction.Start(theStore.Events, Guid.Empty, new DEvent(), new DEvent())]);
    }

    [Fact]
    public async Task fetching_the_empty_guid_reads_nothing_when_there_is_no_such_stream()
    {
        StoreOptions(_ => { });
        await seedTwoStreams();

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamAsync(Guid.Empty)).ShouldBeEmpty();
    }

    [Fact]
    public async Task fetching_the_empty_guid_reads_only_that_stream()
    {
        StoreOptions(_ => { });
        await seedTwoStreams();
        await seedAnEmptyGuidStream();

        await using var query = theStore.QuerySession();
        var events = await query.Events.FetchStreamAsync(Guid.Empty);

        events.Count.ShouldBe(2);
        events.ShouldAllBe(x => x.StreamId == Guid.Empty && x.Data is DEvent);
    }

    [Fact]
    public async Task the_batched_fetch_of_the_empty_guid_reads_nothing_when_there_is_no_such_stream()
    {
        StoreOptions(_ => { });
        await seedTwoStreams();

        await using var query = theStore.QuerySession();
        var batch = query.CreateBatchQuery();
        var fetch = batch.Events.FetchStream(Guid.Empty);
        await batch.Execute();

        (await fetch).ShouldBeEmpty();
    }

    [Fact]
    public async Task aggregating_the_empty_guid_does_not_fold_the_whole_store()
    {
        StoreOptions(_ => { });
        await seedTwoStreams();

        await using var query = theStore.QuerySession();
        (await query.Events.AggregateStreamAsync<Letters>(Guid.Empty)).ShouldBeNull();
    }

    [Fact]
    public async Task fetching_the_latest_of_the_empty_guid_does_not_fold_the_whole_store()
    {
        // Letters has no projection registered here, so this is the live plan's own read
        StoreOptions(_ => { });
        await seedTwoStreams();

        await using var session = theStore.LightweightSession();
        (await session.Events.FetchLatest<Letters>(Guid.Empty)).ShouldBeNull();
    }

    [Fact]
    public async Task compacting_the_empty_guid_is_refused()
    {
        StoreOptions(_ => { });
        await seedTwoStreams();

        await using var session = theStore.LightweightSession();

        var ex = await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => session.Events.CompactStreamAsync<Letters>(Guid.Empty));
        ex.ParamName.ShouldBe("streamId");
    }

    /// <summary>
    ///     The data loss itself, pinned on its own so that it reads as what it is rather than as a missing
    ///     exception: whatever compacting <c>Guid.Empty</c> does, every other stream must come out of it whole.
    /// </summary>
    [Fact]
    public async Task compacting_the_empty_guid_leaves_every_other_stream_alone()
    {
        StoreOptions(_ => { });
        await seedTwoStreams();

        await using (var session = theStore.LightweightSession())
        {
            try
            {
                await session.Events.CompactStreamAsync<Letters>(Guid.Empty);
                await session.SaveChangesAsync();
            }
            catch (ArgumentOutOfRangeException)
            {
                // The refusal; what matters here is what is left afterwards
            }
        }

        await using var query = theStore.QuerySession();

        var first = await query.Events.FetchStreamAsync(_first);
        first.Select(x => x.Data.GetType()).ShouldBe(new[] { typeof(AEvent), typeof(BEvent), typeof(AEvent) });

        var second = await query.Events.FetchStreamAsync(_second);
        second.Select(x => x.Data.GetType()).ShouldBe(new[] { typeof(AEvent), typeof(CEvent) });
    }

    [Fact]
    public async Task compacting_an_empty_guid_stream_that_does_exist_is_refused_too()
    {
        StoreOptions(_ => { });
        await seedTwoStreams();
        await seedAnEmptyGuidStream();

        await using (var session = theStore.LightweightSession())
        {
            await Should.ThrowAsync<ArgumentOutOfRangeException>(
                () => session.Events.CompactStreamAsync<Letters>(Guid.Empty));
        }

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamAsync(Guid.Empty)).Count.ShouldBe(2);
        (await query.Events.FetchStreamAsync(_first)).Count.ShouldBe(3);
    }

    private void useStringIdentity()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Add<LetterCountsByStringProjection>(ProjectionLifecycle.Inline);
        });
    }

    private async Task seedTwoKeyedStreams()
    {
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<LetterCountsByString>("first", new AEvent(), new BEvent(), new AEvent());
        session.Events.StartStream<LetterCountsByString>("second", new AEvent(), new CEvent());
        await session.SaveChangesAsync();
    }

    // A null key names no stream at all -- stream_id is never null -- so a read of it is a read of a stream nobody
    // wrote. The "" cases need a stream nobody wrote under that key, which is the seed below.

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task fetching_an_empty_or_null_key_reads_nothing(string? streamKey)
    {
        useStringIdentity();
        await seedTwoKeyedStreams();

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamAsync(streamKey!)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task the_batched_fetch_of_an_empty_or_null_key_reads_nothing(string? streamKey)
    {
        useStringIdentity();
        await seedTwoKeyedStreams();

        await using var query = theStore.QuerySession();
        var batch = query.CreateBatchQuery();
        var fetch = batch.Events.FetchStream(streamKey!);
        await batch.Execute();

        (await fetch).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task aggregating_an_empty_or_null_key_does_not_fold_the_whole_store(string? streamKey)
    {
        useStringIdentity();
        await seedTwoKeyedStreams();

        await using var query = theStore.QuerySession();
        (await query.Events.AggregateStreamAsync<KeyedLetters>(streamKey!)).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task fetching_the_latest_of_an_empty_or_null_key_does_not_fold_the_whole_store(string? streamKey)
    {
        // KeyedLetters has no projection registered, so this is the live plan's own read
        useStringIdentity();
        await seedTwoKeyedStreams();

        await using var session = theStore.LightweightSession();
        (await session.Events.FetchLatest<KeyedLetters>(streamKey!)).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task the_batched_fetch_latest_of_an_empty_or_null_key_does_not_fold_the_whole_store(string? streamKey)
    {
        // The batch builds its live-plan event query by a different path from the session's
        useStringIdentity();
        await seedTwoKeyedStreams();

        await using var session = theStore.LightweightSession();
        var batch = session.CreateBatchQuery();
        var latest = batch.Events.FetchLatest<KeyedLetters>(streamKey!);
        await batch.Execute();

        (await latest).ShouldBeNull();
    }

    /// <summary>
    ///     Pinned rather than changed: fetching an empty or null key for writing was already refused, by the
    ///     guard on starting a stream, on both of the live plan's paths. It read the whole tenant first.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task fetching_an_empty_or_null_key_for_writing_is_refused(string? streamKey)
    {
        useStringIdentity();
        await seedTwoKeyedStreams();

        await using var session = theStore.LightweightSession();
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => session.Events.FetchForWriting<KeyedLetters>(streamKey!));

        await using var batched = theStore.LightweightSession();
        var batch = batched.CreateBatchQuery();
        var stream = batch.Events.FetchForWriting<KeyedLetters>(streamKey!);
        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
        {
            await batch.Execute();
            await stream;
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task compacting_an_empty_or_null_key_is_refused_and_leaves_every_stream_alone(string? streamKey)
    {
        useStringIdentity();
        await seedTwoKeyedStreams();

        ArgumentOutOfRangeException? refusal = null;
        await using (var session = theStore.LightweightSession())
        {
            try
            {
                await session.Events.CompactStreamAsync<LetterCountsByString>(streamKey!);
                await session.SaveChangesAsync();
            }
            catch (ArgumentOutOfRangeException e)
            {
                refusal = e;
            }
        }

        // What is left first, so that a regression reads as the data loss it is
        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamAsync("first")).Count.ShouldBe(3);
        (await query.Events.FetchStreamAsync("second")).Count.ShouldBe(2);

        refusal.ShouldNotBeNull().ParamName.ShouldBe("streamKey");
    }
}

/// <summary>
///     A string-keyed aggregate with no projection registered, so that its reads take the live plan.
/// </summary>
public class KeyedLetters
{
    public string Id { get; set; } = "";
    public int Count { get; set; }

    public void Apply(AEvent _) => Count++;
    public void Apply(BEvent _) => Count++;
    public void Apply(CEvent _) => Count++;
}
