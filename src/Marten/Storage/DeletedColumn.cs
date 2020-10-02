using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage
{
    internal class DeletedColumn: MetadataColumn, ISelectableColumn
    {
        public DeletedColumn() : base(DocumentMapping.DeletedColumn, "boolean")
        {
            Directive = "DEFAULT FALSE";
            CanAdd = true;
        }

        public override async Task ApplyAsync(DocumentMetadata metadata, int index, DbDataReader reader, CancellationToken token)
        {
            metadata.Deleted = await reader.GetFieldValueAsync<bool>(index, token);
        }

        public override void Apply(DocumentMetadata metadata, int index, DbDataReader reader)
        {
            metadata.Deleted = reader.GetFieldValue<bool>(index);
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async, GeneratedMethod sync,
            int index, DocumentMapping mapping)
        {
            var member = mapping.IsSoftDeletedMember;
            var variableName = "isDeleted";
            var memberType = typeof(bool);

            if (member == null) return;

            sync.Frames.Code($"var {variableName} = reader.GetFieldValue<{memberType.FullNameInCode()}>({index});");
            async.Frames.CodeAsync($"var {variableName} = await reader.GetFieldValueAsync<{memberType.FullNameInCode()}>({index}, token);");

            sync.Frames.SetMemberValue(member, variableName, mapping.DocumentType, generatedType);
            async.Frames.SetMemberValue(member, variableName, mapping.DocumentType, generatedType);
        }

        public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
        {
            return mapping.IsSoftDeletedMember != null;
        }
    }
}
