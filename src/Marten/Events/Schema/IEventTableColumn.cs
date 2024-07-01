using JasperFx.CodeGeneration;

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
}
