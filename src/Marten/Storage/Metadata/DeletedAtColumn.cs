using System;
using System.Reflection;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage.Metadata
{
    internal class DeletedAtColumn: MetadataColumn<DateTimeOffset?> , ISelectableColumn
    {
        public DeletedAtColumn(): base(SchemaConstants.DeletedAtColumn, x => x.DeletedAt)
        {
            CanAdd = true;
            Directive = "NULL";
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async, GeneratedMethod sync,
            int index, DocumentMapping mapping)
        {
            var member = Member;
            var variableName = "deletedAt";
            var memberType = typeof(DateTime);

            if (member == null) return;

            generateIfValueIsNotNull(async, sync, index);

            generateCodeToSetValuesOnDocumentFromReader(generatedType, async, sync, index, mapping, variableName, memberType, member);

            generateCloseScope(async, sync);
        }

        public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
        {
            return Member != null;
        }
    }
}
