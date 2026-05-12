using System;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Events;
using Marten.Internal;
using NpgsqlTypes;
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
        // Generated equivalent of:
        //   var parameter{i} = parameterBuilder.AppendParameter<object>(DBNull.Value);
        //   session.Serializer.WriteToParameter(parameter{i}, evt.Data);
        // Skips the intermediate UTF-16 string allocation that Serializer.ToJson(evt.Data)
        // would emit, instead serializing directly to a sized UTF-8 byte[] bound to the
        // parameter.
        method.Frames.Code($"var parameter{index} = parameterBuilder.{nameof(IGroupedParameterBuilder.AppendParameter)}<object>({typeof(DBNull).FullName}.Value);");
        method.Frames.Code($"{{0}}.Serializer.{nameof(ISerializer.WriteToParameter)}(parameter{index}, {{1}}.{nameof(IEvent.Data)});",
            Use.Type<IMartenSession>(), Use.Type<IEvent>());
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }
}
