using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage
{
    internal class TenantIdColumn: MetadataColumn, ISelectableColumn
    {
        public static new readonly string Name = "tenant_id";

        public TenantIdColumn() : base(Name, "varchar")
        {
            CanAdd = true;
            Directive = $"DEFAULT '{Tenancy.DefaultTenantId}'";
        }

        public override async Task ApplyAsync(DocumentMetadata metadata, int index, DbDataReader reader, CancellationToken token)
        {
            metadata.TenantId = await reader.GetFieldValueAsync<string>(index, token);
        }

        public override void Apply(DocumentMetadata metadata, int index, DbDataReader reader)
        {
            metadata.TenantId = reader.GetFieldValue<string>(index);
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async, GeneratedMethod sync,
            int index, DocumentMapping mapping)
        {
            var member = mapping.TenantIdMember;
            var variableName = "tenantId";
            var memberType = typeof(string);

            if (member == null) return;

            sync.Frames.Code($"var {variableName} = reader.GetFieldValue<{memberType.FullNameInCode()}>({index});");
            async.Frames.CodeAsync($"var {variableName} = await reader.GetFieldValueAsync<{memberType.FullNameInCode()}>({index}, token);");

            sync.Frames.SetMemberValue(member, variableName, mapping.DocumentType, generatedType);
            async.Frames.SetMemberValue(member, variableName, mapping.DocumentType, generatedType);
        }

        public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
        {
            return mapping.TenantIdMember != null;
        }
    }
}
