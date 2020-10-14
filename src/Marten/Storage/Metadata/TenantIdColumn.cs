using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage.Metadata
{
    internal class TenantIdColumn: MetadataColumn<string>, ISelectableColumn
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
    }
}
