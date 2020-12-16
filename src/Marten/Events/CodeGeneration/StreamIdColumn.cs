using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Storage;

namespace Marten.Events.CodeGeneration
{
    internal class StreamIdColumn: TableColumn, IEventTableColumn
    {
        public StreamIdColumn(EventGraph graph) : base("stream_id", "varchar")
        {
            Type = graph.GetStreamIdDBType();
            Directive = graph.TenancyStyle != TenancyStyle.Conjoined
                ? $"REFERENCES {graph.DatabaseSchemaName}.mt_streams ON DELETE CASCADE"
                : null;

        }

        public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
        {
            if (graph.StreamIdentity == StreamIdentity.AsGuid)
            {
                method.AssignMemberFromReader<IEvent>(null, index, x => x.StreamId);
            }
            else
            {
                method.AssignMemberFromReader<IEvent>(null, index, x => x.StreamKey);
            }
        }

        public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
        {
            if (graph.StreamIdentity == StreamIdentity.AsGuid)
            {
                method.AssignMemberFromReaderAsync<IEvent>(null, index, x => x.StreamId);
            }
            else
            {
                method.AssignMemberFromReaderAsync<IEvent>(null, index, x => x.StreamKey);
            }
        }

        public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index)
        {
            if (graph.StreamIdentity == StreamIdentity.AsGuid)
            {
                method.SetParameterFromMember<StreamAction>(index, x => x.Id);
            }
            else
            {
                method.SetParameterFromMember<StreamAction>(index, x => x.Key);
            }
        }
    }
}