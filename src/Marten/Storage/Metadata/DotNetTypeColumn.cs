using LamarCodeGeneration;
using Marten.Events;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage.Metadata
{
    internal class DotNetTypeColumn: MetadataColumn<string>, IEventTableColumn
    {
        public DotNetTypeColumn(): base(SchemaConstants.DotNetTypeColumn, x => x.DotNetType)
        {
            CanAdd = true;
        }

        public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
        {
            throw new System.NotImplementedException();
        }

        public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
        {
            throw new System.NotImplementedException();
        }

        public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index)
        {
            method.SetParameterFromMember<IEvent>(index, x => x.DotNetTypeName);
        }
    }
}
