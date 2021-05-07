using System;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using Marten.Internal;
using Marten.Storage;
using NpgsqlTypes;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema
{
    internal class EventJsonDataColumn: TableColumn, IEventTableColumn
    {
        public EventJsonDataColumn() : base("data", "jsonb")
        {
            AllowNulls = false;
        }

        public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
        {
            throw new NotImplementedException();
        }

        public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
        {
            throw new NotImplementedException();
        }

        public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index)
        {
            method.Frames.Code($"parameters[{index}].NpgsqlDbType = {{0}};", NpgsqlDbType.Jsonb);
            method.Frames.Code($"parameters[{index}].Value = {{0}}.Serializer.ToJson({{1}}.{nameof(IEvent.Data)});", Use.Type<IMartenSession>(), Use.Type<IEvent>());
        }
    }
}
