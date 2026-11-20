#nullable enable
using Marten.Events;

namespace Marten.EventStorage;

/// <summary>
/// SQL fragment helpers shared between the closed-shape event-storage
/// adapter and the Postgres dialect SQL builders. Lives here (rather than
/// the per-mode descriptor classes) because the column ordering is coupled
/// to <see cref="EventDocumentStorage"/>'s
/// <see cref="Marten.Linq.Selectors.ISelector{StreamState}"/> implementation —
/// they must be edited together.
/// </summary>
internal static class StreamStateSql
{
    public static string Build(EventGraph graph) =>
        $"select {Columns(graph)} from {graph.DatabaseSchemaName}.mt_streams";

    /// <summary>
    /// The columns <see cref="EventDocumentStorage"/>'s selector reads, in order. <c>cold_version</c> only
    /// exists with a cold event store, and is only selected then (jasperfx#1211).
    /// </summary>
    public static string Columns(EventGraph graph) => graph.ColdEventStore == null
        ? "id, version, type, timestamp, created, is_archived, compacted_version"
        : "id, version, type, timestamp, created, is_archived, compacted_version, cold_version";

    /// <summary>The number of columns in <see cref="Columns"/>.</summary>
    public static int ColumnCount(EventGraph graph) => graph.ColdEventStore == null ? 7 : 8;
}
