using LamarCodeGeneration;
using Marten.Events;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage.Metadata
{
    internal class TenantIdColumn: MetadataColumn<string>, ISelectableColumn, IEventTableColumn, IStreamTableColumn
    {
        public static new readonly string Name = "tenant_id";

        public TenantIdColumn() : base(Name, x => x.TenantId)
        {
            CanAdd = true;
            Directive = $"DEFAULT '{Tenancy.DefaultTenantId}'";
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async, GeneratedMethod sync,
            int index, DocumentMapping mapping)
        {
            var variableName = "tenantId";
            var memberType = typeof(string);

            if (Member == null) return;

            sync.Frames.Code($"var {variableName} = reader.GetFieldValue<{memberType.FullNameInCode()}>({index});");
            async.Frames.CodeAsync($"var {variableName} = await reader.GetFieldValueAsync<{memberType.FullNameInCode()}>({index}, token);");

            sync.Frames.SetMemberValue(Member, variableName, mapping.DocumentType, generatedType);
            async.Frames.SetMemberValue(Member, variableName, mapping.DocumentType, generatedType);
        }

        public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
        {
            return Member != null;
        }

        public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
        {
            method.IfDbReaderValueIsNotNull(index, () =>
            {
                method.AssignMemberFromReader<IEvent>(null, index, x => x.TenantId);
            });
        }

        public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
        {
            method.IfDbReaderValueIsNotNullAsync(index, () =>
            {
                method.AssignMemberFromReaderAsync<IEvent>(null, index, x => x.TenantId);
            });
        }

        public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index)
        {
            method.SetParameterFromMember<IEvent>(index, x => x.TenantId);
        }

        void IStreamTableColumn.GenerateAppendCode(GeneratedMethod method, int index)
        {
            method.SetParameterFromMember<StreamAction>(index, x => x.TenantId);
        }

        void IStreamTableColumn.GenerateSelectorCodeAsync(GeneratedMethod method, int index)
        {
            throw new System.NotImplementedException();
        }

        void IStreamTableColumn.GenerateSelectorCodeSync(GeneratedMethod method, int index)
        {
            throw new System.NotImplementedException();
        }

        bool IStreamTableColumn.Reads => true;

        bool IStreamTableColumn.Writes => true;
    }
}
