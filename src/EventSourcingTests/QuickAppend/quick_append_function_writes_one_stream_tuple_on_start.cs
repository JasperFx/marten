using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.QuickAppend;

/// <summary>
/// #5456 follow-up. <c>mt_quick_append_events</c> used to write a brand-new stream's
/// <c>mt_streams</c> row twice in one transaction: an INSERT with version 0, then the unconditional
/// trailing UPDATE that sets the final version. Nothing outside the transaction can observe the
/// intermediate 0, so the second tuple was pure write amplification — a measurable cost now that
/// #5456 routes every non-partitioned <c>QuickWithServerTimestamps</c> StartStream through this
/// function. The insert now carries the final version and the UPDATE is skipped for a new stream.
/// </summary>
public class quick_append_function_writes_one_stream_tuple_on_start: OneOffConfigurationsContext
{
    private async Task<DocumentStore> BuildStoreAsync(string label, EventAppendMode mode)
    {
        var schema = $"onetuple_{label}";

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

    private static async Task<string> ReadSoleStreamCtidAsync(DocumentStore store)
    {
        var schema = store.Options.Events.DatabaseSchemaName;
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        return (string)await conn.CreateCommand($"select ctid::text from {schema}.mt_streams")
            .ExecuteScalarAsync();
    }

    /// <remarks>
    /// The assertion reads the row's <c>ctid</c> rather than <c>pg_stat_user_tables.n_tup_upd</c>,
    /// which the stats collector only publishes asynchronously and so cannot be asserted on
    /// deterministically. On a freshly created schema the first heap tuple of the first page is
    /// <c>(0,1)</c>; an insert followed by an in-transaction UPDATE leaves the visible tuple at
    /// <c>(0,2)</c>, because the update writes a second tuple (HOT, so same page). One stream in a
    /// brand-new schema therefore pins "exactly one heap tuple was written" with no timing
    /// dependency at all.
    /// </remarks>
    [Fact]
    public async Task a_new_stream_writes_exactly_one_heap_tuple()
    {
        using var store = await BuildStoreAsync("start", EventAppendMode.QuickWithServerTimestamps);

        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
            await session.SaveChangesAsync();
        }

        (await ReadSoleStreamCtidAsync(store)).ShouldBe("(0,1)");

        await using var query = store.QuerySession();
        var state = await query.Events.FetchStreamStateAsync(streamId);
        state.ShouldNotBeNull();
        state.Version.ShouldBe(2);
    }

    [Fact]
    public async Task appending_to_an_existing_stream_still_updates_the_version()
    {
        using var store = await BuildStoreAsync("append", EventAppendMode.QuickWithServerTimestamps);

        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<SimpleAggregate>(streamId, new AEvent());
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            session.Events.Append(streamId, new BEvent(), new CEvent());
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var state = await query.Events.FetchStreamStateAsync(streamId);
        state.Version.ShouldBe(3);

        var events = await query.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(3);
        events[2].Version.ShouldBe(3);
    }

    [Fact]
    public async Task an_append_with_no_events_leaves_the_existing_version_alone()
    {
        using var store = await BuildStoreAsync("empty", EventAppendMode.QuickWithServerTimestamps);

        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            session.Events.Append(streamId);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var state = await query.Events.FetchStreamStateAsync(streamId);
        state.Version.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_for_writing_against_a_missing_stream_still_versions_from_one()
    {
        using var store = await BuildStoreAsync("f4w", EventAppendMode.QuickWithServerTimestamps);

        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId);
            stream.AppendMany(new AEvent(), new BEvent());
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var events = await query.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(2);
        events[0].Version.ShouldBe(1);
        events[1].Version.ShouldBe(2);

        var state = await query.Events.FetchStreamStateAsync(streamId);
        state.Version.ShouldBe(2);
    }
}
