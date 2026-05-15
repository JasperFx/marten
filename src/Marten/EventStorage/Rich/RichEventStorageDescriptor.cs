#nullable enable
using System;
using JasperFx.Events;
using Marten.Events;

namespace Marten.EventStorage.Rich;

/// <summary>
/// Per-<see cref="EventGraph"/> descriptor for the Rich (full-mode) append
/// flow. Holds only the SQL strings + delegates the Rich operations need —
/// nothing about the Quick paths leaks in.
/// </summary>
/// <remarks>
/// Rich-mode SQL is split into a prefix + suffix because the source-gen
/// output appends parameters inline between them, one per core column plus
/// one virtual <see cref="IEventMetadataBinder.Bind"/> call per active
/// metadata binder. See <see cref="MetadataBinders"/> for the metadata
/// hybrid; see SPIKE.md "Metadata columns" for the rationale.
/// </remarks>
public sealed class RichEventStorageDescriptor
{
    public RichEventStorageDescriptor(
        string appendEventSqlPrefix,
        string appendEventSqlSuffix,
        string insertStreamSql,
        string updateStreamVersionSql,
        string streamStateSelectSql,
        Func<IEvent, string> serializeEventData,
        IEventMetadataBinder[] metadataBinders)
    {
        AppendEventSqlPrefix = appendEventSqlPrefix;
        AppendEventSqlSuffix = appendEventSqlSuffix;
        InsertStreamSql = insertStreamSql;
        UpdateStreamVersionSql = updateStreamVersionSql;
        StreamStateSelectSql = streamStateSelectSql;
        SerializeEventData = serializeEventData;
        MetadataBinders = metadataBinders;
    }

    public string AppendEventSqlPrefix { get; }
    public string AppendEventSqlSuffix { get; }
    public string InsertStreamSql { get; }
    public string UpdateStreamVersionSql { get; }
    public string StreamStateSelectSql { get; }
    public Func<IEvent, string> SerializeEventData { get; }

    /// <summary>
    /// Ordered metadata-column binders. Rich-mode only — Quick-mode
    /// descriptors don't expose this because Quick's metadata binding is
    /// hand-written inline in the source-gen output (per-batch array
    /// parameters; no per-event dispatch worth abstracting).
    /// </summary>
    public IEventMetadataBinder[] MetadataBinders { get; }

    /// <summary>
    /// Whether the events table is conjoined-tenant — every per-stream query
    /// (StreamState lookup, UpdateStreamVersion, etc.) needs a trailing
    /// <c>and tenant_id = $N</c> when this is true.
    /// </summary>
    /// <remarks>
    /// Init-only so the dialect sets it once at descriptor construction.
    /// The codegen path checks <c>graph.TenancyStyle == TenancyStyle.Conjoined</c>
    /// inline; the closed-shape path lifts that to a per-descriptor boolean
    /// so the storage methods don't carry an <see cref="EventGraph"/> reference.
    /// </remarks>
    public bool IsTenancyConjoined { get; init; }
}
