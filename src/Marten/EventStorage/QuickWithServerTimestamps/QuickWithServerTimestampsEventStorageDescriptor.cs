#nullable enable
using System;
using JasperFx.Events;

namespace Marten.EventStorage.QuickWithServerTimestamps;

/// <summary>
/// Per-<see cref="EventGraph"/> descriptor for the Quick batch-append flow
/// in <c>EventAppendMode.QuickWithServerTimestamps</c>. Same shape as
/// <see cref="Quick.QuickEventStorageDescriptor"/>; the SQL differs (extra
/// timestamp-array column in the function signature) and the operation
/// writes one extra array parameter.
/// </summary>
/// <remarks>
/// Could share a base class with <see cref="Quick.QuickEventStorageDescriptor"/>
/// — both have the same set of properties, only the function-call SQL
/// differs. The spike keeps them as separate types for symmetry with the
/// three-storage-class split: <c>RichEventStorage</c> /
/// <c>QuickEventStorage</c> / <c>QuickWithServerTimestampsEventStorage</c>
/// each get their own descriptor with no shared base. If the descriptor
/// shape stays this close once the configuration axes are wired, factoring
/// out a <c>QuickEventStorageDescriptorBase</c> would be reasonable —
/// flagged as a possible cleanup once we've seen the matrix in full.
/// </remarks>
public sealed class QuickWithServerTimestampsEventStorageDescriptor
{
    public QuickWithServerTimestampsEventStorageDescriptor(
        string quickAppendEventsWithServerTimestampsSql,
        string insertStreamSql,
        string updateStreamVersionSql,
        Func<IEvent, string> serializeEventData,
        Func<IEvent, byte[]?> serializeEventBdata)
    {
        QuickAppendEventsWithServerTimestampsSql = quickAppendEventsWithServerTimestampsSql;
        InsertStreamSql = insertStreamSql;
        UpdateStreamVersionSql = updateStreamVersionSql;
        SerializeEventData = serializeEventData;
        SerializeEventBdata = serializeEventBdata;
    }

    /// <summary>
    /// Complete SQL for the <c>select mt_quick_append_events(...)</c> call
    /// with the server-timestamp variant's extra timestamp-array parameter.
    /// </summary>
    public string QuickAppendEventsWithServerTimestampsSql { get; }

    public string InsertStreamSql { get; }
    public string UpdateStreamVersionSql { get; }
    public Func<IEvent, string> SerializeEventData { get; }

    /// <summary>
    ///     #4515: serializer for the <c>bdata</c> bytea column on the per-event
    ///     QuickWithVersion INSERT shape. Binary event types are rejected at
    ///     descriptor-build time in Quick modes, so this always returns
    ///     <c>null</c> — see <see cref="Quick.QuickEventStorageDescriptor.SerializeEventBdata"/>.
    /// </summary>
    public Func<IEvent, byte[]?> SerializeEventBdata { get; }

    /// <summary>Guid stream identity (writeId) vs string identity (writeKey).</summary>
    public bool IsGuidStreamIdentity { get; init; }

    /// <summary>Conjoined-tenant — affects per-stream ops (InsertStream / UpdateStreamVersion / StreamState).</summary>
    public bool IsTenancyConjoined { get; init; }

    /// <summary>
    /// The <c>select version from {schema}.mt_streams where id = </c> prefix for the
    /// AssertStreamVersion (AlwaysEnforceConsistency, zero-events) path. Built once by the dialect.
    /// </summary>
    public string AssertStreamVersionSql { get; init; } = string.Empty;

    /// <summary>Whether the events table has the <c>causation_id</c> column.</summary>
    public bool HasCausationId { get; init; }

    /// <summary>Whether the events table has the <c>correlation_id</c> column.</summary>
    public bool HasCorrelationId { get; init; }

    /// <summary>Whether the events table has the <c>headers</c> jsonb column.</summary>
    public bool HasHeaders { get; init; }

    /// <summary>Whether the events table has the <c>user_name</c> column.</summary>
    public bool HasUserName { get; init; }

    /// <summary>
    /// Whether DCB tag types are configured AND the storage mode wires them
    /// as per-batch <c>varchar[]</c> parameters on <c>mt_quick_append_events</c>.
    /// </summary>
    public bool HasTagWrites { get; init; }

    /// <summary>
    /// #4614: see <see cref="Quick.QuickEventStorageDescriptor.UseTenantPartitionedEvents"/>.
    /// </summary>
    public bool UseTenantPartitionedEvents { get; init; }

    /// <summary>
    /// See <see cref="Quick.QuickEventStorageDescriptor.UseBigIntEvents"/>.
    /// </summary>
    public bool UseBigIntEvents { get; init; }

    /// <summary>
    /// SQL prefix <c>insert into mt_events (cols) values (</c> for the
    /// per-event QuickWithVersion path used by the Quick appender.
    /// </summary>
    public string AppendEventSqlPrefix { get; init; } = string.Empty;

    /// <summary>
    /// SQL suffix including the server-side <c>, nextval(...))</c> seq_id
    /// fragment for the per-event QuickWithVersion path.
    /// </summary>
    public string AppendEventSqlSuffix { get; init; } = ")";

    /// <summary>
    /// Optional-metadata-column binders for the per-event QuickWithVersion
    /// path. Excludes <c>SequenceColumnBinder</c> — seq_id is server-set.
    /// </summary>
    public IEventMetadataBinder[] MetadataBinders { get; init; } = System.Array.Empty<IEventMetadataBinder>();

    /// <summary>
    /// SQL suffix <c>")"</c> for the Full-mode per-event INSERT used by
    /// <c>QuickWithServerTimestampsEventStorage&lt;TId&gt;.AppendEvent</c>
    /// (tombstone path + similar).
    /// </summary>
    public string AppendEventFullSqlSuffix { get; init; } = ")";

    /// <summary>
    /// Metadata binders for the Full-mode per-event INSERT path. Mirror of
    /// <c>RichEventStorageDescriptor.MetadataBinders</c> — seq_id is bound.
    /// </summary>
    public IEventMetadataBinder[] AppendEventFullMetadataBinders { get; init; } = System.Array.Empty<IEventMetadataBinder>();

    /// <summary>
    /// Configures the <c>mt_streams</c> insert command — identical shape to
    /// the Rich descriptor's closure.
    /// </summary>
    public System.Action<Weasel.Postgresql.ICommandBuilder, StreamAction> ConfigureInsertStreamCommand { get; init; }
        = static (_, _) => throw new System.NotSupportedException(
            "QuickWithServerTimestampsEventStorageDescriptor.ConfigureInsertStreamCommand was not installed by the dialect.");

    /// <summary>
    /// Configures the <c>mt_streams</c> update-version command — identical
    /// shape to the Rich descriptor's closure.
    /// </summary>
    public System.Action<Weasel.Postgresql.ICommandBuilder, StreamAction> ConfigureUpdateStreamVersionCommand { get; init; }
        = static (_, _) => throw new System.NotSupportedException(
            "QuickWithServerTimestampsEventStorageDescriptor.ConfigureUpdateStreamVersionCommand was not installed by the dialect.");

    /// <summary>
    /// Creates the batched append operation for a stream. Dialect-installed —
    /// see <see cref="Quick.QuickEventStorageDescriptor.CreateQuickAppendEventsOperation"/>.
    /// </summary>
    public System.Func<QuickWithServerTimestampsEventStorageDescriptor, StreamAction, Marten.Internal.Operations.IStorageOperation>
        CreateQuickAppendEventsOperation { get; init; }
        = static (_, _) => throw new System.NotSupportedException(
            "QuickWithServerTimestampsEventStorageDescriptor.CreateQuickAppendEventsOperation was not installed by the dialect.");
}
