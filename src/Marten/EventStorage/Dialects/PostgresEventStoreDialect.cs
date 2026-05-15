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
using Marten.Storage;
using NpgsqlTypes;
using Weasel.Postgresql;

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
        if (graph.EnableStrictStreamIdentityEnforcement)
        {
            // The strict-identity CTE wraps the mt_streams insert in a
            // modifying CTE chained with a second insert into
            // mt_streams_identity. The CTE shape is non-trivial — its
            // closed-shape port lives behind a separate ticket so we
            // don't ship a half-wired variant. The codegen path still
            // covers this configuration; closed-shape just declines.
            throw new NotSupportedException(
                "EnableStrictStreamIdentityEnforcement isn't yet covered by the closed-shape Rich-mode " +
                "InsertStream operation. Disable the flag or use the codegen path for now. Track on #4412.");
        }

        var (orderedColumns, sqlPrefix) = BuildAppendEventFullColumnsAndPrefix(graph);
        var metadataBinders = SelectRichMetadataBinders(orderedColumns);
        var isConjoined = graph.TenancyStyle == TenancyStyle.Conjoined;
        var isGuid = graph.StreamIdentity == StreamIdentity.AsGuid;

        return new RichEventStorageDescriptor(
            appendEventSqlPrefix: sqlPrefix,
            appendEventSqlSuffix: ")",
            insertStreamSql: BuildInsertStreamSql(graph),
            updateStreamVersionSql: BuildUpdateStreamVersionSql(graph),
            streamStateSelectSql: EventDocumentStorageGenerator.BuildStreamStateSelectSql(graph),
            serializeEventData: e => serializer.ToJson(e.Data),
            metadataBinders: metadataBinders)
        {
            IsTenancyConjoined = isConjoined,
            IsGuidStreamIdentity = isGuid,
            ConfigureInsertStreamCommand = BuildInsertStreamCommandConfigurer(graph, isConjoined, isGuid),
            ConfigureUpdateStreamVersionCommand = BuildUpdateStreamVersionCommandConfigurer(graph, isConjoined, isGuid),
        };
    }

    /// <summary>
    /// Builds the per-call closure that issues the <c>insert into mt_streams</c>
    /// command. Mirrors <c>EventDocumentStorageGenerator.buildInsertStream</c>
    /// for the non-strict-identity case (the strict-identity CTE variant
    /// rejects early at descriptor-build time — see <see cref="BuildRichDescriptor"/>).
    /// </summary>
    /// <remarks>
    /// Column order on the codegen path comes from
    /// <c>StreamsTable.Columns.OfType&lt;IStreamTableColumn&gt;().Where(x => x.Writes)</c>.
    /// For the vanilla configuration that's:
    /// <list type="bullet">
    ///   <item>Conjoined: <c>tenant_id, id, type, version</c></item>
    ///   <item>Single tenant: <c>id, type, version, tenant_id</c></item>
    /// </list>
    /// (<c>timestamp</c> and <c>created</c> are <c>Writes=false</c>;
    /// <c>is_archived</c> isn't an <c>IStreamTableColumn</c>.)
    /// </remarks>
    private static Action<ICommandBuilder, StreamAction> BuildInsertStreamCommandConfigurer(
        EventGraph graph, bool isConjoined, bool isGuid)
    {
        var sqlPrefix = BuildInsertStreamSqlPrefix(graph, isConjoined);

        if (isConjoined)
        {
            if (isGuid)
            {
                return (builder, stream) =>
                {
                    builder.Append(sqlPrefix);
                    var pb = builder.CreateGroupedParameterBuilder(',');
                    pb.AppendParameter(stream.TenantId, NpgsqlDbType.Varchar);
                    pb.AppendParameter(stream.Id, NpgsqlDbType.Uuid);
                    pb.AppendParameter(stream.AggregateTypeName, NpgsqlDbType.Varchar);
                    pb.AppendParameter(stream.Version, NpgsqlDbType.Bigint);
                    builder.Append(")");
                };
            }

            return (builder, stream) =>
            {
                builder.Append(sqlPrefix);
                var pb = builder.CreateGroupedParameterBuilder(',');
                pb.AppendParameter(stream.TenantId, NpgsqlDbType.Varchar);
                pb.AppendParameter(stream.Key, NpgsqlDbType.Varchar);
                pb.AppendParameter(stream.AggregateTypeName, NpgsqlDbType.Varchar);
                pb.AppendParameter(stream.Version, NpgsqlDbType.Bigint);
                builder.Append(")");
            };
        }

        if (isGuid)
        {
            return (builder, stream) =>
            {
                builder.Append(sqlPrefix);
                var pb = builder.CreateGroupedParameterBuilder(',');
                pb.AppendParameter(stream.Id, NpgsqlDbType.Uuid);
                pb.AppendParameter(stream.AggregateTypeName, NpgsqlDbType.Varchar);
                pb.AppendParameter(stream.Version, NpgsqlDbType.Bigint);
                pb.AppendParameter(stream.TenantId, NpgsqlDbType.Varchar);
                builder.Append(")");
            };
        }

        return (builder, stream) =>
        {
            builder.Append(sqlPrefix);
            var pb = builder.CreateGroupedParameterBuilder(',');
            pb.AppendParameter(stream.Key, NpgsqlDbType.Varchar);
            pb.AppendParameter(stream.AggregateTypeName, NpgsqlDbType.Varchar);
            pb.AppendParameter(stream.Version, NpgsqlDbType.Bigint);
            pb.AppendParameter(stream.TenantId, NpgsqlDbType.Varchar);
            builder.Append(")");
        };
    }

    /// <summary>
    /// SQL prefix for <c>insert into {schema}.mt_streams (cols...) values (</c>
    /// — column list matches the bind order produced by
    /// <see cref="BuildInsertStreamCommandConfigurer"/>.
    /// </summary>
    private static string BuildInsertStreamSqlPrefix(EventGraph graph, bool isConjoined)
    {
        // Mirror StreamsTable column ordering — TenantIdColumn is PK-first
        // for conjoined; otherwise it lands after the always-on columns.
        var cols = isConjoined
            ? "tenant_id, id, type, version"
            : "id, type, version, tenant_id";

        return $"insert into {graph.DatabaseSchemaName}.{StreamsTable.TableName} ({cols}) values (";
    }

    /// <summary>
    /// Closure for <c>update mt_streams set version = $1 where id = $2 and version = $3 [and tenant_id = $4] returning version</c>.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>EventDocumentStorageGenerator.buildUpdateStreamVersion</c>.
    /// The base <see cref="UpdateStreamVersion"/>.Postprocess raises
    /// <c>EventStreamUnexpectedMaxEventIdException</c> when zero rows are
    /// affected (the expected-version guard fired).
    /// </remarks>
    private static Action<ICommandBuilder, StreamAction> BuildUpdateStreamVersionCommandConfigurer(
        EventGraph graph, bool isConjoined, bool isGuid)
    {
        var setVersionPrefix = $"update {graph.DatabaseSchemaName}.{StreamsTable.TableName} set version = ";

        return (builder, stream) =>
        {
            builder.Append(setVersionPrefix);
            // Single GroupedParameterBuilder with no separator — we
            // interleave AppendSql between each parameter manually.
            var pb = builder.CreateGroupedParameterBuilder();
            pb.AppendParameter(stream.Version, NpgsqlDbType.Bigint);

            builder.Append(" where id = ");
            if (isGuid)
                pb.AppendParameter(stream.Id, NpgsqlDbType.Uuid);
            else
                pb.AppendParameter(stream.Key, NpgsqlDbType.Varchar);

            builder.Append(" and version = ");
            pb.AppendParameter(stream.ExpectedVersionOnServer!.Value, NpgsqlDbType.Bigint);

            if (isConjoined)
            {
                builder.Append(" and tenant_id = ");
                pb.AppendParameter(stream.TenantId, NpgsqlDbType.Varchar);
            }

            builder.Append(" returning version");
        };
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
        // As each metadata axis lands its binder (#4416), its case here flips
        // from default-throw to a real binder. Any column name still in the
        // default branch throws NotSupportedException so we fail loudly
        // rather than silently mismatch parameter count vs column count.
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

                case "causation_id":
                    binders.Add(new CausationIdColumnBinder());
                    break;

                case "correlation_id":
                    binders.Add(new CorrelationIdColumnBinder());
                    break;

                case "user_name":
                    binders.Add(new UserNameColumnBinder());
                    break;

                case "headers":
                    // Write-side is wired (HeadersColumnBinder serializes via
                    // session.Serializer.ToJson). Read-side still throws on
                    // HeadersColumn.ReadValueSync because the IEventTableColumn
                    // read surface doesn't yet thread ISerializer. Tracked as
                    // #4416 part 2.
                    binders.Add(new HeadersColumnBinder());
                    break;

                // TODO (#4416): remaining binders:
                //   "timestamp"     → TimestampColumnBinder (server-set via now() in some modes; read-back capable)
                //   "tags"          → TagsColumnBinder (HSTORE; Postgres-specific)
                //   "is_skipped"    → IsSkippedColumnBinder (depends on EnableEventSkippingInProjectionsOrSubscriptions)

                default:
                    throw new NotSupportedException(
                        $"No closed-shape Rich-mode binder for the '{column.Name}' column. " +
                        $"This event-store configuration (e.g., tags / event-skipping) " +
                        $"isn't covered yet by the closed-shape hierarchy — disable the relevant " +
                        $"StoreOptions.Events.Metadata or DcbStorageMode flag, or wait for the binder to land per #4416.");
            }
        }

        return binders.ToArray();
    }

    /// <summary>
    /// "Core" columns are the ones whose writes get inlined directly in
    /// <see cref="Rich.RichAppendEventOperation.ConfigureCommand"/> — always
    /// present, always bound as scalars, no configuration variance.
    /// Everything else is treated as metadata and routes through
    /// <see cref="IEventMetadataBinder"/>.
    /// </summary>
    /// <remarks>
    /// MUST stay in lockstep with the inlined-bind list in
    /// <see cref="Rich.RichAppendEventOperation.ConfigureCommand"/>. Adding
    /// a column here without an inlined bind (or vice versa) leaves the
    /// parameter count off by one against the SQL prefix's column list.
    /// </remarks>
    private static bool IsCoreColumn(IEventTableColumn column) =>
        column.Name is "id" or "stream_id" or "stream_key" or "version"
            or "data" or "type" or "tenant_id" or "mt_dotnet_type"
            or "timestamp";

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
