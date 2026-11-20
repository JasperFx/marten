using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

// #6114: after a high water skip, a shard whose progress is past an event has not necessarily processed it. An event
// that committed late inside the skipped range sits below every shard's progress until late event recovery records
// it, so compaction waits while an open skip has a recorded gap holding one of the events it would delete.
public class stream_compacting_and_open_high_water_skips: OneOffConfigurationsContext
{
    public sealed record Counted(Guid TallyId, int Amount);

    public sealed class Counter
    {
        public Guid Id { get; set; }
        public int Total { get; set; }

        public static Counter Create(Counted e) => new() { Total = e.Amount };

        public void Apply(Counted e) => Total += e.Amount;
    }

    public sealed class KeyedCounter
    {
        public string Id { get; set; } = null!;
        public int Total { get; set; }

        public static KeyedCounter Create(Counted e) => new() { Total = e.Amount };

        public void Apply(Counted e) => Total += e.Amount;
    }

    public sealed class Tally
    {
        public Guid Id { get; set; }
        public int Total { get; set; }
    }

    public sealed class TallyProjection: MultiStreamProjection<Tally, Guid>
    {
        public TallyProjection()
        {
            Identity<Counted>(x => x.TallyId);
        }

        public void Apply(Tally tally, Counted e) => tally.Total += e.Amount;
    }

    // An async multi-stream projection, which is what a late event deleted by compaction would be lost to
    private async Task configure(StreamIdentity identity, bool advancedTracking = true)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = identity;
            opts.Events.EnableAdvancedAsyncTracking = advancedTracking;
            opts.Projections.Add(new TallyProjection(), ProjectionLifecycle.Async);
        });
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    private sealed record Stream(object Id, long First, long Last);

    // Three events in one stream, then the projection catches up, so the shard lag guard has nothing to say
    private async Task<Stream> threeCountedEventsAllProcessed(StreamIdentity identity)
    {
        var tally = Guid.NewGuid();
        object id = identity == StreamIdentity.AsGuid ? Guid.NewGuid() : "counter-" + Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            var events = new object[] { new Counted(tally, 1), new Counted(tally, 2), new Counted(tally, 3) };
            if (id is Guid guid)
            {
                session.Events.StartStream<Counter>(guid, events);
            }
            else
            {
                session.Events.StartStream<KeyedCounter>((string)id, events);
            }

            await session.SaveChangesAsync();
        }

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(TimeSpan.FromSeconds(30));
            await daemon.StopAllAsync();
        }

        var sequences = (await fetch(id)).Select(x => x.Sequence).ToArray();
        return new Stream(id, sequences.Min(), sequences.Max());
    }

    private async Task<System.Collections.Generic.IReadOnlyList<IEvent>> fetch(object id)
    {
        await using var query = theStore.QuerySession();
        return id is Guid guid
            ? await query.Events.FetchStreamAsync(guid)
            : await query.Events.FetchStreamAsync((string)id);
    }

    private async Task<int> countedEventsIn(Stream stream) => (await fetch(stream.Id)).Count(x => x.Data is Counted);

    private async Task compact(Stream stream)
    {
        await using var session = theStore.LightweightSession();
        if (stream.Id is Guid guid)
        {
            await session.Events.CompactStreamAsync<Counter>(guid);
        }
        else
        {
            await session.Events.CompactStreamAsync<KeyedCounter>((string)stream.Id);
        }

        await session.SaveChangesAsync();
    }

    // A row the daemon would write when the store-global high water mark skips (starting, ending], with the sequence
    // numbers that had no committed event at the time as the recorded gaps
    private async Task recordSkip(long starting, long ending, long[] gaps, bool closed = false)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"insert into {theStore.Events.DatabaseSchemaName}.mt_high_water_skips " +
            "(ending_sequence, tenant_id, starting_sequence, reason, gaps, timestamp, closed_at) " +
            "values (@ending, '', @starting, 'LiveTransaction', @gaps, now(), case when @closed then now() end)", conn);
        cmd.Parameters.AddWithValue("ending", ending);
        cmd.Parameters.AddWithValue("starting", starting);
        cmd.Parameters.AddWithValue("gaps", gaps);
        cmd.Parameters.AddWithValue("closed", closed);
        await cmd.ExecuteNonQueryAsync();
    }

    [Theory]
    [InlineData(StreamIdentity.AsGuid)]
    [InlineData(StreamIdentity.AsString)]
    public async Task an_open_skip_with_the_events_in_its_gaps_refuses_compaction(StreamIdentity identity)
    {
        await configure(identity);
        var stream = await threeCountedEventsAllProcessed(identity);

        // The three events committed late, inside the gaps of a skip that recovery is still checking
        await recordSkip(stream.First - 1, stream.Last + 5, [stream.First, stream.Last + 5]);

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => compact(stream));
        refused.Message.ShouldContain($"({stream.First - 1}, {stream.Last + 5}]");
        refused.Message.ShouldContain(string.Join(", ", Enumerable.Range((int)stream.First, 3)));
        refused.Message.ShouldContain("LiveTransaction");
        refused.Message.ShouldContain("LateEventDetectionWindow");
        refused.Message.ShouldContain("Nothing was compacted");

        (await countedEventsIn(stream)).ShouldBe(3);
    }

    [Theory]
    [InlineData(StreamIdentity.AsGuid)]
    [InlineData(StreamIdentity.AsString)]
    public async Task a_closed_skip_allows_compaction(StreamIdentity identity)
    {
        await configure(identity);
        var stream = await threeCountedEventsAllProcessed(identity);

        await recordSkip(stream.First - 1, stream.Last + 5, [stream.First, stream.Last + 5], closed: true);

        await compact(stream);
        (await countedEventsIn(stream)).ShouldBe(0);
    }

    [Theory]
    [InlineData(StreamIdentity.AsGuid)]
    [InlineData(StreamIdentity.AsString)]
    public async Task an_open_skip_outside_the_events_allows_compaction(StreamIdentity identity)
    {
        await configure(identity);
        var stream = await threeCountedEventsAllProcessed(identity);

        await recordSkip(stream.Last + 100, stream.Last + 200, [stream.Last + 101, stream.Last + 200]);

        await compact(stream);
        (await countedEventsIn(stream)).ShouldBe(0);
    }

    [Theory]
    [InlineData(StreamIdentity.AsGuid)]
    [InlineData(StreamIdentity.AsString)]
    public async Task an_open_skip_over_the_events_whose_gaps_miss_them_allows_compaction(StreamIdentity identity)
    {
        await configure(identity);
        var stream = await threeCountedEventsAllProcessed(identity);

        // The range covers the events, but they had committed when it was skipped: only the numbers above them
        // were missing, so the projection read them
        await recordSkip(stream.First - 1, stream.Last + 5, [stream.Last + 1, stream.Last + 5]);

        await compact(stream);
        (await countedEventsIn(stream)).ShouldBe(0);
    }

    [Fact]
    public async Task open_skips_are_not_checked_without_advanced_async_tracking()
    {
        // Nothing would ever close the skip, and no recovery would look for the late event anyway
        await configure(StreamIdentity.AsGuid, advancedTracking: false);
        var stream = await threeCountedEventsAllProcessed(StreamIdentity.AsGuid);

        await recordSkip(stream.First - 1, stream.Last + 5, [stream.First, stream.Last + 5]);

        await compact(stream);
        (await countedEventsIn(stream)).ShouldBe(0);
    }
}
