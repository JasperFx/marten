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
        string streamStateSelectSql,
        Func<IEvent, string> serializeEventData)
    {
        QuickAppendEventsWithServerTimestampsSql = quickAppendEventsWithServerTimestampsSql;
        InsertStreamSql = insertStreamSql;
        UpdateStreamVersionSql = updateStreamVersionSql;
        StreamStateSelectSql = streamStateSelectSql;
        SerializeEventData = serializeEventData;
    }

    /// <summary>
    /// Complete SQL for the <c>select mt_quick_append_events(...)</c> call
    /// with the server-timestamp variant's extra timestamp-array parameter.
    /// </summary>
    public string QuickAppendEventsWithServerTimestampsSql { get; }

    public string InsertStreamSql { get; }
    public string UpdateStreamVersionSql { get; }
    public string StreamStateSelectSql { get; }
    public Func<IEvent, string> SerializeEventData { get; }
}
