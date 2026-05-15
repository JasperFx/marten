#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Schema;

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
    /// Reader columns for the closed-shape read path (#4411). Each column's
    /// <see cref="IEventTableColumn.ReadValueSync"/> /
    /// <see cref="IEventTableColumn.ReadValueAsync"/> is invoked per row in
    /// <c>ClosedShapeEventDocumentStorage.ApplyReaderDataToEvent</c>.
    /// </summary>
    /// <remarks>
    /// Iteration starts at ordinal 3 — positions 0/1/2 (data / type /
    /// mt_dotnet_type) are read by the base <c>ISelector&lt;IEvent&gt;</c>
    /// implementation in <c>EventDocumentStorage</c>, mirroring the codegen
    /// path's <c>Resolve</c> body. The dialect populates this list from
    /// <see cref="EventsTable.SelectColumns"/> minus the first three.
    /// Internal: <see cref="IEventTableColumn"/> is an internal seam.
    /// </remarks>
    internal IReadOnlyList<IEventTableColumn> ReaderColumns { get; init; } = Array.Empty<IEventTableColumn>();
}
