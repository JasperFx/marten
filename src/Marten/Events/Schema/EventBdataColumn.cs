#nullable enable
using System.Data.Common;
using JasperFx.Events;
using Marten.Services;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

/// <summary>
///     Sibling to <see cref="EventJsonDataColumn"/> for binary event payloads
///     (<see href="https://github.com/JasperFx/marten/issues/4515">#4515</see>).
///     For event types opted in to binary serialization, the JSON <c>data</c>
///     column holds the literal <c>'{}'::jsonb</c> placeholder and the actual
///     payload lives here as raw bytes.
/// </summary>
/// <remarks>
///     <para>
///         Nullable on purpose: when an event row uses JSON serialization,
///         <c>data</c> holds the full payload and <c>bdata</c> is <c>NULL</c>.
///         The presence of a non-null <c>bdata</c> is the on-row discriminator.
///         This is what makes the feature additive — existing JSON rows in an
///         upgraded store have <c>bdata = NULL</c> and continue to read through
///         the JSON path.
///     </para>
///     <para>
///         <strong>Read-path note:</strong> the bytes are consumed up in
///         <see cref="Marten.Events.EventDocumentStorage.Resolve(System.Data.Common.DbDataReader)"/>
///         /
///         <see cref="Marten.Events.EventDocumentStorage.ResolveAsync(System.Data.Common.DbDataReader, System.Threading.CancellationToken)"/>
///         (the per-row JSON-vs-binary dispatch point), so this column's
///         <c>ReadValueSync</c> / <c>ReadValueAsync</c> are deliberately
///         no-ops in the per-column metadata loop — they'd otherwise re-read
///         the same bytes for nothing.
///     </para>
/// </remarks>
internal sealed class EventBdataColumn: TableColumn, IEventTableColumn
{
    public EventBdataColumn(): base("bdata", "bytea")
    {
        AllowNulls = true;
    }

    public string ValueSql(EventGraph graph, AppendMode mode) => "?";

    void IEventTableColumn.ReadValueSync(DbDataReader reader, int index, IEvent @event)
    {
        // No-op — consumed in EventDocumentStorage.Resolve to choose the
        // JSON-vs-binary deserialization path.
    }

    System.Threading.Tasks.Task IEventTableColumn.ReadValueAsync(
        DbDataReader reader, int index, IEvent @event, System.Threading.CancellationToken cancellation)
        => System.Threading.Tasks.Task.CompletedTask;
}
