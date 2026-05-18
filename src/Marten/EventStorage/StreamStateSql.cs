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
        $"select id, version, type, timestamp, created, is_archived from {graph.DatabaseSchemaName}.mt_streams";
}
