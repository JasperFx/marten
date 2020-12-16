using System;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Storage;

namespace Marten.Events.CodeGeneration
{
    internal class EventTypeColumn: TableColumn, IEventTableColumn
    {
        public EventTypeColumn() : base("type", "varchar(500)", "NOT NULL")
        {
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
            method.SetParameterFromMember<IEvent>(index, x => x.EventTypeName);
        }
    }
}