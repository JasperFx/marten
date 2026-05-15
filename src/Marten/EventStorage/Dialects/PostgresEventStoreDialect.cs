#nullable enable
using System.Linq;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Archiving;
using Marten.Events.CodeGeneration;
using Marten.Events.Schema;

namespace Marten.EventStorage.Dialects;

/// <summary>
/// Postgres implementation of <see cref="IEventStoreSqlDialect"/>. Produces
/// the same SQL the codegen path emits today — just composed once at startup
/// rather than baked into emitted method bodies.
/// </summary>
/// <remarks>
/// <para>
/// Spike scope (#4404 W4): only <see cref="AppendEventFullPrefix"/> and
/// <see cref="AppendEventFullSuffix"/> are filled in to show the shape.
/// The remaining templates are stubbed and return placeholder strings —
/// extending them is mechanical (copy the column-list assembly from
/// <c>EventDocumentStorageGenerator</c>'s emit sites and produce the same
/// SQL as concatenated strings).
/// </para>
/// </remarks>
internal sealed class PostgresEventStoreDialect: IEventStoreSqlDialect
{
    public string AppendEventFullPrefix(EventGraph graph)
    {
        // Mirrors EventDocumentStorageGenerator.buildAppendEventOperation
        // line ~263. Same column ordering: writeable columns from
        // EventsTable.SelectColumns(), minus IsArchivedColumn, with the
        // SequenceColumn pushed to the end so nextval() runs after the
        // explicit binds.
        var columns = new EventsTable(graph)
            .SelectColumns()
            .Where(x => x is not IsArchivedColumn)
            .ToList();

        var sequence = columns.OfType<SequenceColumn>().Single();
        columns.Remove(sequence);
        columns.Add(sequence);

        return $"insert into {graph.DatabaseSchemaName}.mt_events (" +
               columns.Select(c => c.Name).Join(", ") +
               ") values (";
    }

    public string AppendEventFullSuffix(EventGraph graph) => ")";

    public string AppendEventQuickWithVersion(EventGraph graph)
        => $"-- TODO (W4 spike): port from EventDocumentStorageGenerator.buildAppendEventOperation(QuickWithVersion)";

    public string QuickAppendEvents(EventGraph graph)
        => $"-- TODO (W4 spike): port from EventDocumentStorageGenerator.buildQuickAppendOperation (non-server-timestamp branch)";

    public string QuickAppendEventsWithServerTimestamps(EventGraph graph)
        => $"-- TODO (W4 spike): port from EventDocumentStorageGenerator.buildQuickAppendOperation (server-timestamp branch)";

    public string InsertStream(EventGraph graph)
        => $"-- TODO (W4 spike): port from EventDocumentStorageGenerator.buildInsertStream";

    public string UpdateStreamVersion(EventGraph graph)
        => $"-- TODO (W4 spike): port from EventDocumentStorageGenerator.buildUpdateStreamVersion";

    public string StreamStateSelect(EventGraph graph)
        // The codegen path already has a public helper that returns the
        // stream-state SELECT — reuse it directly so this spike doesn't
        // duplicate the column list.
        => EventDocumentStorageGenerator.BuildStreamStateSelectSql(graph);
}
