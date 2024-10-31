using System;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using Marten.Internal;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core.Operations;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

internal class EventJsonDataColumn: TableColumn, IEventTableColumn
{
    public EventJsonDataColumn(): base("data", "jsonb")
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
        method.Frames.Code($"var parameter{index} = parameterBuilder.{nameof(IGroupedParameterBuilder<NpgsqlParameter, NpgsqlDbType>.AppendParameter)}({{0}}.Serializer.ToJson({{1}}.{nameof(IEvent.Data)}));",
             Use.Type<IMartenSession>(), Use.Type<IEvent>());

        method.Frames.Code($"parameter{index}.NpgsqlDbType = {{0}};", NpgsqlDbType.Jsonb);
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }
}
