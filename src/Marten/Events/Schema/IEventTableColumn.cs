using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Services;

namespace Marten.Events.Schema;

public enum AppendMode
{
    Full,
    QuickWithVersion
}

/// <summary>
///     Describes a column on <c>mt_events</c> for the closed-shape event-storage
///     adapter. Implementations expose the SQL fragment used to write the column
///     plus runtime read/write delegates — no codegen.
/// </summary>
internal interface IEventTableColumn
{
    /// <summary>
    ///     Column name
    /// </summary>
    string Name { get; }

    string ValueSql(EventGraph graph, AppendMode mode);

    /// <summary>
    ///     Read this column's value from the given reader ordinal and assign it
    ///     onto the event. Used by
    ///     <c>ClosedShapeEventDocumentStorage.ApplyReaderDataToEvent</c>.
    /// </summary>
    void ReadValueSync(DbDataReader reader, int index, IEvent @event)
        => throw new System.NotImplementedException(
            $"Column '{Name}' ({GetType().Name}) does not implement ReadValueSync.");

    /// <summary>
    ///     Asynchronous twin of <see cref="ReadValueSync(DbDataReader, int, IEvent)"/>.
    /// </summary>
    Task ReadValueAsync(DbDataReader reader, int index, IEvent @event, CancellationToken cancellation)
        => throw new System.NotImplementedException(
            $"Column '{Name}' ({GetType().Name}) does not implement ReadValueAsync.");

    /// <summary>
    ///     Serializer-aware read for columns that need session-level state to
    ///     deserialize a row value (today: <c>HeadersColumn</c> — jsonb →
    ///     <c>Dictionary&lt;string,object&gt;</c> via <see cref="ISerializer"/>.
    ///     Npgsql can't map that pair without a serializer hook).
    /// </summary>
    /// <remarks>
    ///     Default implementation delegates to the parameterless
    ///     <see cref="ReadValueSync(DbDataReader, int, IEvent)"/>. Concrete
    ///     columns that DO need the serializer override this method instead
    ///     of the parameterless one; everyone else stays on the
    ///     parameterless override. #4416 part 2.
    /// </remarks>
    void ReadValueSync(DbDataReader reader, int index, IEvent @event, ISerializer serializer)
        => ReadValueSync(reader, index, @event);

    /// <summary>
    ///     Asynchronous twin of the serializer-aware
    ///     <see cref="ReadValueSync(DbDataReader, int, IEvent, ISerializer)"/>.
    /// </summary>
    Task ReadValueAsync(DbDataReader reader, int index, IEvent @event, ISerializer serializer, CancellationToken cancellation)
        => ReadValueAsync(reader, index, @event, cancellation);
}
