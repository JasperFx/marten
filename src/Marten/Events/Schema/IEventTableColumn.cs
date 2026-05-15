using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Events;
using Marten.Services;

namespace Marten.Events.Schema;

public enum AppendMode
{
    Full,
    QuickWithVersion
}

/// <summary>
///     This interface is used by the event store code generation to build the IEventStorage
/// </summary>
internal interface IEventTableColumn
{
    /// <summary>
    ///     Column name
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Generate the synchronous IEventSelector code for this event table column
    /// </summary>
    /// <param name="method"></param>
    /// <param name="graph"></param>
    /// <param name="index"></param>
    public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index);

    /// <summary>
    ///     Generate the asynchronous IEventSelector code for this event table column
    /// </summary>
    /// <param name="method"></param>
    /// <param name="graph"></param>
    /// <param name="index"></param>
    public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index);

    /// <summary>
    ///     Generate code for this column to capture the NpgsqlParameter value that should
    ///     be persisted when appending an event to the events table
    /// </summary>
    /// <param name="method"></param>
    /// <param name="graph"></param>
    /// <param name="index"></param>
    /// <param name="full"></param>
    public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index, AppendMode full);

    string ValueSql(EventGraph graph, AppendMode mode);

    /// <summary>
    ///     Runtime equivalent of <see cref="GenerateSelectorCodeSync"/> — reads this
    ///     column's value from the given reader ordinal and assigns it onto the event.
    ///     Used by the closed-shape event-storage hierarchy (#4410 W4) so the read
    ///     path doesn't depend on runtime codegen.
    /// </summary>
    /// <remarks>
    ///     Default implementation throws — concrete column types override. The
    ///     codegen path (today's default) doesn't call this; only the closed-shape
    ///     <c>ClosedShapeEventDocumentStorage.ApplyReaderDataToEvent</c> does.
    ///     Tracking issue: #4411.
    /// </remarks>
    void ReadValueSync(DbDataReader reader, int index, IEvent @event)
        => throw new System.NotImplementedException(
            $"Column '{Name}' ({GetType().Name}) does not implement ReadValueSync. " +
            $"See #4411 — every concrete IEventTableColumn needs this runtime port of GenerateSelectorCodeSync.");

    /// <summary>
    ///     Asynchronous twin of <see cref="ReadValueSync"/>.
    /// </summary>
    Task ReadValueAsync(DbDataReader reader, int index, IEvent @event, CancellationToken cancellation)
        => throw new System.NotImplementedException(
            $"Column '{Name}' ({GetType().Name}) does not implement ReadValueAsync. " +
            $"See #4411 — every concrete IEventTableColumn needs this runtime port of GenerateSelectorCodeAsync.");

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
