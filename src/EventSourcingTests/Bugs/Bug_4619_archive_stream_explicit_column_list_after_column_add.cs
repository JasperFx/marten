using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #4619 — mt_archive_stream emits INSERT INTO mt_events / mt_streams without
/// an explicit column list, relying on positional alignment between the in-memory
/// EventsTable / StreamsTable model and the physical table. ALTER TABLE ADD
/// COLUMN appends physically at the END (Postgres can't reposition columns),
/// so after any column-add migration the model order diverges from the physical
/// order — and the positional INSERT misaligns. The user-visible failure
/// (introduced by the #4515 bdata column addition on upgrade) is:
///
/// <code>
/// 42804: column "timestamp" is of type timestamp with time zone but expression
///   is of type character varying
///   Where: PL/pgSQL function mt_archive_stream(character varying) line 4 at
///     SQL statement
/// </code>
///
/// <para>
/// Repro: simulate the upgrade by manually appending a no-op column to
/// mt_events after schema creation, then archive a stream. With the fix
/// (explicit column list on both INSERT target and SELECT source), the archive
/// is robust to physical-order drift.
/// </para>
/// </summary>
public class Bug_4619_archive_stream_explicit_column_list_after_column_add
{
    [Fact]
    public async Task archive_stream_succeeds_after_a_post_creation_column_add()
    {
        var schema = $"bug4619_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            try { await conn.DropSchemaAsync(schema); } catch { }
        }

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            // UseArchivedStreamPartitioning routes archives through the bulk
            // function variant (writeWithPartitioning) — the broken positional
            // INSERT path. Without this flag the simple UPDATE path is used
            // and the bug doesn't trigger.
            opts.Events.UseArchivedStreamPartitioning = true;

            opts.Events.AddEventType<ArchiveProbeEvent>();
        });

        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // Seed a stream.
        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(streamId, new ArchiveProbeEvent("first"));
            await session.SaveChangesAsync();
        }

        // Simulate the reporter's upgrade scenario directly: drop bdata from the
        // physical mt_events table and re-add it. Postgres ALTER TABLE ADD COLUMN
        // always appends physically at the end (it can't reposition columns), so
        // after this round-trip the model still has bdata at position 4 (after
        // `data`) but the physical layout has bdata at the END — exactly the
        // shape an upgrade to Marten 9.5.1 produces on a pre-9.5.1 mt_events
        // table. From position 5 (`type` in model) onward, model and physical
        // column orders diverge by one, and the positional INSERT in
        // mt_archive_stream misaligns column-by-column.
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.CreateCommand($"alter table {schema}.mt_events drop column bdata").ExecuteNonQueryAsync();
            await conn.CreateCommand($"alter table {schema}.mt_events add column bdata bytea null").ExecuteNonQueryAsync();
        }

        // Archive — the positional INSERT inside mt_archive_stream would
        // misalign by one column and trip 42804. With the fix (explicit
        // column list), the SELECT and INSERT both name the columns
        // explicitly so the appended column is harmlessly excluded.
        await using (var session = store.LightweightSession())
        {
            session.Events.ArchiveStream(streamId);
            await session.SaveChangesAsync();
        }

        // Sanity: the stream is archived.
        await using (var query = store.QuerySession())
        {
            var state = await query.Events.FetchStreamStateAsync(streamId);
            state.ShouldNotBeNull();
            state!.IsArchived.ShouldBeTrue();
        }
    }

    public record ArchiveProbeEvent(string Label);
}
