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
using Marten.Storage.Metadata;
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
        // #4427: strict-identity enforcement wraps the mt_streams insert in
        // a modifying CTE that chains an insert into the non-partitioned
        // mt_streams_identity tracking table. Without the CTE the codegen
        // path's collision detection across archive-partition boundaries
        // doesn't fire under UseArchivedStreamPartitioning, because the
        // active partition's unique index doesn't span partitions. The
        // resulting unique-violation surfaces with
        // TableName = mt_streams_identity, which InsertStreamBase.matches()
        // already translates into ExistingStreamIdCollisionException —
        // hence "implement the CTE; do not invent a new exception path."
        var (sqlPrefix, sqlSuffix) = graph.EnableStrictStreamIdentityEnforcement
            ? BuildStrictIdentityInsertStreamSql(graph, isConjoined)
            : (BuildInsertStreamSqlPrefix(graph, isConjoined), ")");

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
                    builder.Append(sqlSuffix);
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
                builder.Append(sqlSuffix);
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
                builder.Append(sqlSuffix);
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
            builder.Append(sqlSuffix);
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
    /// SQL prefix + suffix pair for the strict-identity-enforcement variant
    /// of the <c>mt_streams</c> insert (#4427). Wraps the vanilla insert in
    /// a modifying CTE that chains an insert into
    /// <see cref="StreamIdentityEnforcementTable"/>:
    /// <code>
    /// with new_stream as (insert into mt_streams (...) values (...) returning &lt;identity_cols&gt;)
    /// insert into mt_streams_identity (&lt;identity_cols&gt;) select &lt;identity_cols&gt; from new_stream
    /// </code>
    /// The chained insert raises <c>UniqueViolation</c> with
    /// <c>TableName = mt_streams_identity</c> on collision, which
    /// <c>InsertStreamBase.matches()</c> already maps to
    /// <c>ExistingStreamIdCollisionException</c>. No new parameters — the
    /// CTE pipes the just-inserted identity through.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>EventDocumentStorageGenerator.buildInsertStream</c>'s
    /// strict-identity branch — column order is exactly what
    /// <see cref="BuildInsertStreamSqlPrefix"/> produces, so the
    /// per-variant parameter-bind code is unchanged.
    /// </remarks>
    private static (string Prefix, string Suffix) BuildStrictIdentityInsertStreamSql(
        EventGraph graph, bool isConjoined)
    {
        var identityCols = isConjoined ? "tenant_id, id" : "id";
        var streamsCols = isConjoined
            ? "tenant_id, id, type, version"
            : "id, type, version, tenant_id";

        var prefix = $"with new_stream as (insert into {graph.DatabaseSchemaName}.{StreamsTable.TableName} " +
                     $"({streamsCols}) values (";

        var suffix = $") returning {identityCols}) " +
                     $"insert into {graph.DatabaseSchemaName}.{StreamIdentityEnforcementTable.TableName} ({identityCols}) " +
                     $"select {identityCols} from new_stream";

        return (prefix, suffix);
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
        var (isConjoined, isGuid, hasCausation, hasCorrelation, hasHeaders, hasUserName, hasTagWrites)
            = ReadQuickFlags(graph);

        var (orderedColumns, appendEventSqlPrefix) = BuildAppendEventFullColumnsAndPrefix(graph);
        var quickWithVersionSuffix = BuildAppendEventQuickWithVersionSuffix(graph);
        var quickMetadataBinders = SelectQuickModeMetadataBinders(orderedColumns);
        var fullMetadataBinders = SelectRichMetadataBinders(orderedColumns);

        return new QuickEventStorageDescriptor(
            quickAppendEventsSql: BuildQuickAppendEventsSql(graph, serverTimestamps: false),
            insertStreamSql: BuildInsertStreamSql(graph),
            updateStreamVersionSql: BuildUpdateStreamVersionSql(graph),
            streamStateSelectSql: EventDocumentStorageGenerator.BuildStreamStateSelectSql(graph),
            serializeEventData: e => serializer.ToJson(e.Data))
        {
            IsGuidStreamIdentity = isGuid,
            IsTenancyConjoined = isConjoined,
            HasCausationId = hasCausation,
            HasCorrelationId = hasCorrelation,
            HasHeaders = hasHeaders,
            HasUserName = hasUserName,
            HasTagWrites = hasTagWrites,
            ConfigureInsertStreamCommand = BuildInsertStreamCommandConfigurer(graph, isConjoined, isGuid),
            ConfigureUpdateStreamVersionCommand = BuildUpdateStreamVersionCommandConfigurer(graph, isConjoined, isGuid),
            AppendEventSqlPrefix = appendEventSqlPrefix,
            AppendEventSqlSuffix = quickWithVersionSuffix,
            MetadataBinders = quickMetadataBinders,
            AppendEventFullSqlSuffix = ")",
            AppendEventFullMetadataBinders = fullMetadataBinders,
        };
    }

    public QuickWithServerTimestampsEventStorageDescriptor BuildQuickWithServerTimestampsDescriptor(
        EventGraph graph, ISerializer serializer)
    {
        var (isConjoined, isGuid, hasCausation, hasCorrelation, hasHeaders, hasUserName, hasTagWrites)
            = ReadQuickFlags(graph);

        var (orderedColumns, appendEventSqlPrefix) = BuildAppendEventFullColumnsAndPrefix(graph);
        var quickWithVersionSuffix = BuildAppendEventQuickWithVersionSuffix(graph);
        var quickMetadataBinders = SelectQuickModeMetadataBinders(orderedColumns);
        var fullMetadataBinders = SelectRichMetadataBinders(orderedColumns);

        return new QuickWithServerTimestampsEventStorageDescriptor(
            quickAppendEventsWithServerTimestampsSql: BuildQuickAppendEventsSql(graph, serverTimestamps: true),
            insertStreamSql: BuildInsertStreamSql(graph),
            updateStreamVersionSql: BuildUpdateStreamVersionSql(graph),
            streamStateSelectSql: EventDocumentStorageGenerator.BuildStreamStateSelectSql(graph),
            serializeEventData: e => serializer.ToJson(e.Data))
        {
            IsGuidStreamIdentity = isGuid,
            IsTenancyConjoined = isConjoined,
            HasCausationId = hasCausation,
            HasCorrelationId = hasCorrelation,
            HasHeaders = hasHeaders,
            HasUserName = hasUserName,
            HasTagWrites = hasTagWrites,
            ConfigureInsertStreamCommand = BuildInsertStreamCommandConfigurer(graph, isConjoined, isGuid),
            ConfigureUpdateStreamVersionCommand = BuildUpdateStreamVersionCommandConfigurer(graph, isConjoined, isGuid),
            AppendEventSqlPrefix = appendEventSqlPrefix,
            AppendEventSqlSuffix = quickWithVersionSuffix,
            MetadataBinders = quickMetadataBinders,
            AppendEventFullSqlSuffix = ")",
            AppendEventFullMetadataBinders = fullMetadataBinders,
        };
    }

    /// <summary>
    /// SQL suffix for the per-event <c>QuickWithVersion</c> path used by the
    /// Quick appender. Carries the seq_id <c>nextval(...)</c> server-side
    /// literal + closing paren. Mirrors
    /// <c>SequenceColumn.ValueSql(graph, AppendMode.QuickWithVersion)</c>:
    /// <c>nextval('{schema}.mt_events_sequence')</c>.
    /// </summary>
    private static string BuildAppendEventQuickWithVersionSuffix(EventGraph graph)
        => $", nextval('{graph.DatabaseSchemaName}.mt_events_sequence'))";

    /// <summary>
    /// Picks the metadata binders for the Quick-paths' per-event
    /// QuickWithVersion INSERT. Same shape as
    /// <see cref="SelectRichMetadataBinders"/> EXCEPT
    /// <c>SequenceColumnBinder</c> is excluded — seq_id comes from the
    /// server-side <c>nextval(...)</c> literal in the SQL suffix, not a
    /// bound parameter.
    /// </summary>
    private static IEventMetadataBinder[] SelectQuickModeMetadataBinders(IReadOnlyList<IEventTableColumn> orderedColumns)
    {
        var binders = new List<IEventMetadataBinder>(4);
        foreach (var column in orderedColumns)
        {
            if (IsCoreColumn(column)) continue;

            switch (column.Name)
            {
                // seq_id: handled by the SQL suffix (nextval literal); NO binder.
                case "seq_id":
                case "mt_events_sequence":
                    continue;

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
                    binders.Add(new HeadersColumnBinder());
                    break;

                default:
                    throw new NotSupportedException(
                        $"No closed-shape Quick-mode QuickWithVersion binder for the '{column.Name}' column. " +
                        $"Disable the relevant StoreOptions.Events.Metadata or DcbStorageMode flag, " +
                        $"or wait for the binder to land per #4416.");
            }
        }

        return binders.ToArray();
    }

    /// <summary>
    /// Reads the configuration flags that the Quick paths' operations care about.
    /// Mirrors the conditional code-emit gates in
    /// <c>EventDocumentStorageGenerator.buildQuickAppendOperation</c>:
    /// presence of optional metadata columns, tenancy style, stream identity,
    /// and the DCB tag-write predicate (<c>TagTypes.Count &gt; 0 &amp;&amp;
    /// DcbStorageMode != HStore</c>; HStore mode writes tags via a follow-up
    /// UPDATE, see <c>QuickEventAppender</c>).
    /// </summary>
    private static (bool IsConjoined, bool IsGuid, bool HasCausation, bool HasCorrelation,
        bool HasHeaders, bool HasUserName, bool HasTagWrites) ReadQuickFlags(EventGraph graph)
    {
        var table = new EventsTable(graph);
        return (
            IsConjoined: graph.TenancyStyle == TenancyStyle.Conjoined,
            IsGuid: graph.StreamIdentity == StreamIdentity.AsGuid,
            HasCausation: table.Columns.OfType<CausationIdColumn>().Any(),
            HasCorrelation: table.Columns.OfType<CorrelationIdColumn>().Any(),
            HasHeaders: table.Columns.OfType<HeadersColumn>().Any(),
            HasUserName: table.Columns.OfType<UserNameColumn>().Any(),
            HasTagWrites: graph.TagTypes.Count > 0 && graph.DcbStorageMode != DcbStorageMode.HStore);
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

    /// <summary>
    /// Builds the SQL prefix <c>select {schema}.mt_quick_append_events(</c>.
    /// The trailing parameter list and closing <c>)</c> are appended by the
    /// operation's <c>ConfigureCommand</c> via <c>IGroupedParameterBuilder</c>.
    /// </summary>
    /// <remarks>
    /// The function signature is generated in
    /// <see cref="QuickAppendEventFunction.WriteCreateStatement"/> based on
    /// the configured metadata + tag-type axes; the closed-shape operation
    /// reads the same axes off the descriptor flags
    /// (<see cref="QuickEventStorageDescriptor.HasCausationId"/> etc.) to
    /// emit parameters in matching order.
    /// </remarks>
    private static string BuildQuickAppendEventsSql(EventGraph graph, bool serverTimestamps)
    {
        // serverTimestamps is implicit in the descriptor type (the operation
        // calls writeTimestamps when on QuickWithServerTimestamps), so we
        // don't bake the flag into the SQL prefix here — only the SQL
        // function signature on the database needs to know, and that's the
        // QuickAppendEventFunction's responsibility, not the dialect's.
        _ = serverTimestamps;
        return $"select {graph.DatabaseSchemaName}.mt_quick_append_events(";
    }

    /// <summary>
    /// Read-only convenience for the InsertStream SQL summary used by the
    /// descriptor's <c>InsertStreamSql</c> property. The actual SQL is
    /// produced per-call by
    /// <see cref="BuildInsertStreamCommandConfigurer"/>; this string is
    /// just informational / diagnostic.
    /// </summary>
    private static string BuildInsertStreamSql(EventGraph graph) =>
        $"insert into {graph.DatabaseSchemaName}.{StreamsTable.TableName} (...) values (...)";

    /// <summary>
    /// Symmetric informational summary for UpdateStreamVersion. The
    /// <see cref="BuildUpdateStreamVersionCommandConfigurer"/> closure
    /// produces the actual SQL.
    /// </summary>
    private static string BuildUpdateStreamVersionSql(EventGraph graph) =>
        $"update {graph.DatabaseSchemaName}.{StreamsTable.TableName} set version = ... where ... returning version";
}
