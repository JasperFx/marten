using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage.Metadata
{
    internal class SoftDeletedColumn: MetadataColumn<bool>, ISelectableColumn
    {
        public SoftDeletedColumn() : base(SchemaConstants.DeletedColumn, x => x.Deleted)
        {
            Directive = "DEFAULT FALSE";
            CanAdd = true;
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async, GeneratedMethod sync,
            int index, DocumentMapping mapping)
        {
            var variableName = "isDeleted";
            var memberType = typeof(bool);

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
