using System.Data.Common;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage
{
    internal class DocumentTypeColumn: MetadataColumn<string>, ISelectableColumn
    {
        public DocumentTypeColumn(DocumentMapping mapping) : base(SchemaConstants.DocumentTypeColumn, x => x.DocumentType)
        {
            CanAdd = true;
            Directive = $"DEFAULT '{mapping.AliasFor(mapping.DocumentType)}'";
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async,
            GeneratedMethod sync, int index,
            DocumentMapping mapping)
        {
            var variableName = "docType";
            var memberType = typeof(string);

            if (Member == null) return;

            sync.Frames.Code($"var {variableName} = reader.GetFieldValue<{memberType.FullNameInCode()}>({index});");
            async.Frames.CodeAsync($"var {variableName} = await reader.GetFieldValueAsync<{memberType.FullNameInCode()}>({index}, token);");

            sync.Frames.SetMemberValue(Member, variableName, mapping.DocumentType, generatedType);
            async.Frames.SetMemberValue(Member, variableName, mapping.DocumentType, generatedType);
        }

        public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
        {
            return true;
        }

    }
}
