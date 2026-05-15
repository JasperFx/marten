#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using JasperFx.Events;
using Marten.EventStorage.Metadata;
using Marten.EventStorage.Quick;
using Marten.EventStorage.QuickWithServerTimestamps;
using Marten.EventStorage.Rich;
using Marten.Events;
using Marten.Events.Archiving;
using Marten.Events.CodeGeneration;
using Marten.Events.Schema;
using Marten.Services;

namespace Marten.EventStorage.Dialects;

/// <summary>
/// Postgres implementation of <see cref="IEventStoreSqlDialect"/>. Produces
/// the same SQL the codegen path emits today — composed once at startup
/// rather than emitted into method bodies.
/// </summary>
/// <remarks>
/// <para>
/// Column ordering matches
/// <see cref="EventDocumentStorageGenerator.buildAppendEventOperation"/>:
/// <see cref="EventsTable.SelectColumns"/> sequence, minus the
/// <see cref="IsArchivedColumn"/> (select-only), with the
/// <see cref="SequenceColumn"/> moved to the end so its server-side
/// <c>nextval()</c> runs after the explicit binds. The Rich-mode metadata
/// binder list aligns to that ordering — the dialect builds both in
/// lockstep so the SQL and the parameter binds stay in sync.
/// </para>
/// </remarks>
internal sealed class PostgresEventStoreDialect: IEventStoreSqlDialect
{
    public RichEventStorageDescriptor BuildRichDescriptor(EventGraph graph, ISerializer serializer)
    {
        var (orderedColumns, sqlPrefix) = BuildAppendEventFullColumnsAndPrefix(graph);
        var metadataBinders = SelectRichMetadataBinders(orderedColumns);

        return new RichEventStorageDescriptor(
            appendEventSqlPrefix: sqlPrefix,
            appendEventSqlSuffix: ")",
            insertStreamSql: BuildInsertStreamSql(graph),
            updateStreamVersionSql: BuildUpdateStreamVersionSql(graph),
            streamStateSelectSql: EventDocumentStorageGenerator.BuildStreamStateSelectSql(graph),
            serializeEventData: e => serializer.ToJson(e.Data),
            metadataBinders: metadataBinders);
    }

    public QuickEventStorageDescriptor BuildQuickDescriptor(EventGraph graph, ISerializer serializer)
    {
        return new QuickEventStorageDescriptor(
            quickAppendEventsSql: BuildQuickAppendEventsSql(graph, serverTimestamps: false),
            insertStreamSql: BuildInsertStreamSql(graph),
            updateStreamVersionSql: BuildUpdateStreamVersionSql(graph),
            streamStateSelectSql: EventDocumentStorageGenerator.BuildStreamStateSelectSql(graph),
            serializeEventData: e => serializer.ToJson(e.Data));
    }

    public QuickWithServerTimestampsEventStorageDescriptor BuildQuickWithServerTimestampsDescriptor(
        EventGraph graph, ISerializer serializer)
    {
        return new QuickWithServerTimestampsEventStorageDescriptor(
            quickAppendEventsWithServerTimestampsSql: BuildQuickAppendEventsSql(graph, serverTimestamps: true),
            insertStreamSql: BuildInsertStreamSql(graph),
            updateStreamVersionSql: BuildUpdateStreamVersionSql(graph),
            streamStateSelectSql: EventDocumentStorageGenerator.BuildStreamStateSelectSql(graph),
            serializeEventData: e => serializer.ToJson(e.Data));
    }

    /// <summary>
    /// Mirrors <c>EventDocumentStorageGenerator.buildAppendEventOperation</c>:
    /// <c>EventsTable.SelectColumns()</c> minus <see cref="IsArchivedColumn"/>,
    /// with <see cref="SequenceColumn"/> pushed to the end. Returns the
    /// ordered column list (used to pick the matching metadata binders)
    /// AND the composed SQL prefix (ending at <c>VALUES (</c>).
    /// </summary>
    private static (IReadOnlyList<IEventTableColumn> Columns, string Sql) BuildAppendEventFullColumnsAndPrefix(EventGraph graph)
    {
        var columns = new EventsTable(graph)
            .SelectColumns()
            .Where(x => x is not IsArchivedColumn)
            .ToList();

        var sequence = columns.OfType<SequenceColumn>().Single();
        columns.Remove(sequence);
        columns.Add(sequence);

        var prefix = $"insert into {graph.DatabaseSchemaName}.mt_events (" +
                     columns.Select(c => c.Name).Join(", ") +
                     ") values (";

        return (columns, prefix);
    }

    /// <summary>
    /// Picks the <see cref="IEventMetadataBinder"/> for each column past the
    /// core slice (id, stream_id/key, version, data, type, tenant_id,
    /// mt_dotnet_type). Order matches the dialect's column ordering, so the
    /// operation's per-column bind sequence (inlined core writes + metadata
    /// binder loop) stays aligned with the SQL.
    /// </summary>
    private static IEventMetadataBinder[] SelectRichMetadataBinders(IReadOnlyList<IEventTableColumn> orderedColumns)
    {
        // Selection by column NAME (not CLR type) because Marten's event-store
        // column model uses a single generic `EventTableColumn` class for
        // most columns — the optional metadata columns
        // (headers / causation_id / correlation_id / user_name / tags) are
        // added via `events.Metadata.X` config objects, not as distinct
        // CLR types. Switching on Name is the stable contract.
        //
        // For the v9 first cut this is intentionally narrow: SequenceColumn
        // (always present, server-set via nextval, read-back path) is the
        // only binder wired in. Any other non-core column on the SQL prefix
        // throws NotSupportedException so we never silently mismatch
        // parameter count vs column count. As each metadata axis lands its
        // binder (#4410 follow-ups), the switch grows.
        var binders = new List<IEventMetadataBinder>(8);

        foreach (var column in orderedColumns)
        {
            if (IsCoreColumn(column)) continue;

            switch (column.Name)
            {
                case "seq_id":
                case "mt_events_sequence":
                    binders.Add(new SequenceColumnBinder());
                    break;

                // TODO (#4410): per-binder cases as the binders land:
                //   "timestamp"     → TimestampColumnBinder (server-set via now() in some modes; read-back capable)
                //   "headers"       → HeadersColumnBinder
                //   "causation_id"  → CausationIdColumnBinder
                //   "correlation_id"→ CorrelationIdColumnBinder
                //   "user_name"     → UserNameColumnBinder
                //   "tags"          → TagsColumnBinder (HSTORE; Postgres-specific)
                //   "is_skipped"    → IsSkippedColumnBinder (depends on EnableEventSkippingInProjectionsOrSubscriptions)

                default:
                    throw new NotSupportedException(
                        $"No closed-shape Rich-mode binder for the '{column.Name}' column. " +
                        $"This event-store configuration (e.g., headers / causation / correlation / username / tags / event-skipping) " +
                        $"isn't covered yet by the closed-shape hierarchy — disable the relevant " +
                        $"StoreOptions.Events.Metadata or DcbStorageMode flag, or wait for the binder to land per #4410.");
            }
        }

        return binders.ToArray();
    }

    /// <summary>
    /// "Core" columns are the ones whose writes get inlined directly in the
    /// closed-shape operation body — always present, always bound as
    /// scalars, no configuration variance. Everything else is treated as
    /// metadata and routes through <see cref="IEventMetadataBinder"/>.
    /// </summary>
    private static bool IsCoreColumn(IEventTableColumn column) =>
        column.Name is "id" or "stream_id" or "stream_key" or "version"
            or "data" or "type" or "tenant_id" or "mt_dotnet_type";

    // ---- Quick / QuickWithServerTimestamps / InsertStream / UpdateStreamVersion ----
    //
    // Spike-era TODO stubs. The Rich path is being built out first
    // (#4410 commit sequence). When the Quick paths are wired, these
    // helpers port from EventDocumentStorageGenerator.buildQuickAppendOperation
    // / buildInsertStream / buildUpdateStreamVersion. Until then the
    // generated SQL is intentionally invalid so attempting to use them
    // fails loudly rather than silently.

    private static string BuildQuickAppendEventsSql(EventGraph graph, bool serverTimestamps)
    {
        // TODO (#4410): port full implementation from
        // EventDocumentStorageGenerator.buildQuickAppendOperation. The
        // SQL is `select <schema>.mt_quick_append_events(<args>)` with a
        // configuration-aware argument list. The serverTimestamps flag
        // toggles inclusion of the per-batch timestamp array.
        var schema = graph.DatabaseSchemaName;
        var prefix = serverTimestamps ? "/* server-timestamps */" : string.Empty;
        return $"-- TODO (#4410): {schema}.mt_quick_append_events(...) — Quick path not yet wired. {prefix}";
    }

    private static string BuildInsertStreamSql(EventGraph graph)
    {
        // TODO (#4410): port from EventDocumentStorageGenerator.buildInsertStream.
        return $"-- TODO (#4410): insert into {graph.DatabaseSchemaName}.mt_streams (...) values (...) — not yet wired.";
    }

    private static string BuildUpdateStreamVersionSql(EventGraph graph)
    {
        // TODO (#4410): port from EventDocumentStorageGenerator.buildUpdateStreamVersion.
        return $"-- TODO (#4410): update {graph.DatabaseSchemaName}.mt_streams set version = ... where ... — not yet wired.";
    }
}
