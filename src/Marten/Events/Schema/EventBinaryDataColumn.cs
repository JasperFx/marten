using System;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Events;
using Marten.Internal;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

/// <summary>
///     Represents the 'data' column as bytea for binary event serialization (e.g., MemoryPack).
///     Used instead of <see cref="EventJsonDataColumn"/> when <see cref="EventGraph.UseMemoryPackSerialization"/> is enabled.
/// </summary>
internal class EventBinaryDataColumn: TableColumn, IEventTableColumn
{
    public EventBinaryDataColumn(): base("data", "bytea")
    {
        AllowNulls = false;
    }

    public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
    {
        throw new NotSupportedException();
    }

    public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
    {
        throw new NotSupportedException();
    }

    public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index, AppendMode full)
    {
        method.Frames.Code(
            $"var parameter{index} = parameterBuilder.{nameof(IGroupedParameterBuilder.AppendParameter)}({{0}}.Options.EventGraph.BinarySerializer.Serialize({{1}}.{nameof(IEvent.EventType)}, {{1}}.{nameof(IEvent.Data)}));",
            Use.Type<IMartenSession>(), Use.Type<IEvent>());

        method.Frames.Code($"parameter{index}.NpgsqlDbType = {{0}};", NpgsqlDbType.Bytea);
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }
}
