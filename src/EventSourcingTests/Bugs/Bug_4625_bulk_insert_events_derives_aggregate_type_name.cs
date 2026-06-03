using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #4625 — <c>BulkInsertEventsAsync</c> (via <c>BulkEventAppender</c>) writes
/// <c>mt_streams.type</c> from <c>StreamAction.AggregateTypeName</c>, but never
/// derives that string from the public <c>StreamAction.AggregateType</c>. The
/// only path that populates <c>AggregateTypeName</c> is
/// <c>StreamAction.PrepareEvents</c> — and that's NOT called on the bulk path
/// (it would also dequeue sequence values + assign Sequence/Version/Timestamp,
/// work the bulk importer does itself). Result: bulk-inserting a stream with
/// <c>action.AggregateType = typeof(T)</c> set produces <c>mt_streams.type = NULL</c>.
///
/// <para>
/// Under <c>UseMandatoryStreamTypeDeclaration</c>, the migrated stream then
/// has no type, so subsequent appends are rejected and the stream can't
/// participate in single-stream aggregation by type. Fix: derive
/// <c>AggregateTypeName</c> from <c>AggregateType</c> in <c>BulkEventAppender</c>
/// before writing the row, so the public API surface is honored without
/// callers needing to reach for the <c>internal set</c>.
/// </para>
/// </summary>
public class Bug_4625_bulk_insert_events_derives_aggregate_type_name
{
    [Fact]
    public async Task BulkInsertEventsAsync_writes_mt_streams_type_from_AggregateType()
    {
        var schema = $"bug4625_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            try { await conn.DropSchemaAsync(schema); } catch { }
        }

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.AddEventType<SomethingHappened>();
        });

        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // The repro shape from the issue: build a StreamAction explicitly, set
        // AggregateType via the PUBLIC setter (not the internal AggregateTypeName
        // setter), and bulk-insert. Pre-fix, mt_streams.type ends up NULL.
        var streamKey = "stream-" + Guid.NewGuid().ToString("N")[..10];
        var action = StreamAction.Start(store.Events, streamKey, new SomethingHappened("first"));
        action.AggregateType = typeof(MyAggregate);
        action.TenantId = "alpha";

        await store.BulkInsertEventsAsync("alpha", new[] { action });

        // Read mt_streams.type back directly — assert it carries the alias.
        await using var conn2 = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn2.OpenAsync();
        await using var cmd = conn2.CreateCommand(
            $"select type from {schema}.mt_streams where id = @id and tenant_id = @tid");
        cmd.Parameters.AddWithValue("id", streamKey);
        cmd.Parameters.AddWithValue("tid", "alpha");
        var typeName = (string?)await cmd.ExecuteScalarAsync();

        typeName.ShouldNotBeNull(
            "BulkInsertEventsAsync must derive mt_streams.type from StreamAction.AggregateType — " +
            "pre-fix this was NULL because BulkEventAppender only ever read AggregateTypeName " +
            "(which is populated only inside StreamAction.PrepareEvents, NOT called on the bulk path)");
        typeName.ShouldBe(store.Events.AggregateAliasFor(typeof(MyAggregate)));
    }

    [Fact]
    public async Task bulk_inserted_stream_with_AggregateType_supports_subsequent_Append_under_mandatory_stream_type_declaration()
    {
        // The downstream consequence the issue calls out: with
        // UseMandatoryStreamTypeDeclaration on, a bulk-inserted stream whose
        // type was lost (NULL) can't take any further appends — the mandatory
        // guard fires on every subsequent operation. Post-fix, the bulk-inserted
        // stream's type IS set, so appends work.
        var schema = $"bug4625b_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            try { await conn.DropSchemaAsync(schema); } catch { }
        }

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseMandatoryStreamTypeDeclaration = true;
            opts.Events.AddEventType<SomethingHappened>();
        });

        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        var streamKey = "stream-" + Guid.NewGuid().ToString("N")[..10];
        var action = StreamAction.Start(store.Events, streamKey, new SomethingHappened("first"));
        action.AggregateType = typeof(MyAggregate);
        action.TenantId = "alpha";

        await store.BulkInsertEventsAsync("alpha", new[] { action });

        // Now append more events to the bulk-inserted stream — must succeed
        // because mt_streams.type was correctly set to the MyAggregate alias.
        await using var session = store.LightweightSession("alpha");
        session.Events.Append(streamKey, new SomethingHappened("second"));
        await session.SaveChangesAsync();

        await using var query = store.QuerySession("alpha");
        var events = await query.Events.FetchStreamAsync(streamKey);
        events.Count.ShouldBe(2);
    }

    public record SomethingHappened(string Label);

    public class MyAggregate
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}
