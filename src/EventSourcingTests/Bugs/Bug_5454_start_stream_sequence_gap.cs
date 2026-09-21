using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #5454 — under <see cref="EventAppendMode.Quick"/> / <see cref="EventAppendMode.QuickWithServerTimestamps"/>,
/// a <c>StartStream</c> took the per-event INSERT route, where <c>seq_id</c> comes from an inlined
/// <c>nextval()</c> that is never read back. The events therefore kept <c>Sequence</c> 0, and
/// <c>EventGraph.TryCreateTombstoneBatch</c> skips those ("don't even try to save a tombstone if you don't
/// know the sequence"). Any failure LATER in the same batch — the reported case is a losing
/// <c>UpdateRevision</c>, and a duplicate document INSERT is the same shape — rolled the transaction back
/// with those sequence numbers drawn and nothing to fill them, so the async daemon's high-water detector
/// stalled for every tenant in the database until it had seen the gap for StaleSequenceThreshold.
///
/// An append to an EXISTING stream never had this problem: the bulk <c>mt_quick_append_events</c> function
/// returns the sequences, which is what makes the rollback tombstoneable. The fix routes a Start through the
/// same function, with <c>expected_version</c> 0 standing in for the dedicated INSERT's unique violation.
/// </summary>
public class Bug_5454_start_stream_sequence_gap: OneOffConfigurationsContext
{
    private async Task<DocumentStore> BuildStoreAsync(string label, EventAppendMode mode)
    {
        var schema = $"b5454_{label}_{(mode == EventAppendMode.Quick ? "q" : "qst")}";

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.CreateCommand($"drop schema if exists {schema} cascade").ExecuteNonQueryAsync();
        }

        var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.AppendMode = mode;
        });

        _disposables.Add(store);
        return store;
    }

    private static async Task<(long lastValue, long maxSeq, long tombstones)> ReadSequenceStateAsync(
        DocumentStore store)
    {
        var schema = store.Options.Events.DatabaseSchemaName;
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var lastValue = Convert.ToInt64(
            await conn.CreateCommand($"select last_value from {schema}.mt_events_sequence").ExecuteScalarAsync());

        var maxObj = await conn.CreateCommand($"select max(seq_id) from {schema}.mt_events").ExecuteScalarAsync();
        var maxSeq = maxObj is null or DBNull ? 0L : Convert.ToInt64(maxObj);

        var tombstones = Convert.ToInt64(await conn
            .CreateCommand($"select count(*) from {schema}.mt_events where type = 'tombstone'")
            .ExecuteScalarAsync());

        return (lastValue, maxSeq, tombstones);
    }

    /// <remarks>
    /// <see cref="EventAppendMode.Quick"/> is deliberately absent: the function stamps
    /// <c>(now() at time zone 'utc')</c> for that mode and takes no timestamps array, so a Start routed
    /// through it would drop a caller's overridden <c>IEvent.Timestamp</c> — which
    /// <c>overriding_metadata.override_timestamp_on_start(mode: Quick)</c> pins. Plain Quick therefore keeps
    /// the per-event route, and keeps the gap, until that timestamp contract is decided.
    /// </remarks>
    [Theory]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task a_start_stream_whose_batch_fails_afterwards_leaves_no_unfilled_sequence_number(
        EventAppendMode mode)
    {
        using var store = await BuildStoreAsync("gap", mode);

        // The document whose second INSERT will fail the batch AFTER the event inserts have drawn their
        // sequence numbers (UnitOfWork.AllOperations runs event operations first).
        var target = Target.Random();
        await using (var seed = store.LightweightSession())
        {
            seed.Insert(target);
            await seed.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<SimpleAggregate>(Guid.NewGuid(), new AEvent(), new BEvent());
            session.Insert(target);

            var ex = await Record.ExceptionAsync(() => session.SaveChangesAsync());
            ex.ShouldNotBeNull();
        }

        var (lastValue, maxSeq, tombstones) = await ReadSequenceStateAsync(store);

        // The two numbers the rolled-back StartStream drew are filled by tombstones, so the high-water
        // detector sees no gap. This assertion FAILS on master: lastValue is 2 ahead of maxSeq and no
        // tombstone was written, because the events carried no sequence to write one for.
        tombstones.ShouldBe(2);
        lastValue.ShouldBe(maxSeq);
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task a_start_stream_on_an_id_already_in_use_still_collides_and_draws_no_sequence(
        EventAppendMode mode)
    {
        using var store = await BuildStoreAsync("collide", mode);

        var streamId = Guid.NewGuid();
        await using (var seed = store.LightweightSession())
        {
            seed.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
            await seed.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<SimpleAggregate>(streamId, new CEvent());
            await Should.ThrowAsync<ExistingStreamIdCollisionException>(() => session.SaveChangesAsync());
        }

        // The function checks the version before any nextval(), so the collision advances nothing and
        // needs no tombstone.
        var (lastValue, maxSeq, tombstones) = await ReadSequenceStateAsync(store);
        lastValue.ShouldBe(maxSeq);
        tombstones.ShouldBe(0);
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task a_start_stream_that_succeeds_still_versions_from_one(EventAppendMode mode)
    {
        using var store = await BuildStoreAsync("happy", mode);

        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new CEvent());
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var state = await query.Events.FetchStreamStateAsync(streamId);
        state.ShouldNotBeNull();
        state.Version.ShouldBe(3);
        state.AggregateType.ShouldBe(typeof(SimpleAggregate));

        var events = await query.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(3);
        events[0].Version.ShouldBe(1);
        events[2].Version.ShouldBe(3);
        events[0].Sequence.ShouldBeLessThan(events[2].Sequence);
    }
}
