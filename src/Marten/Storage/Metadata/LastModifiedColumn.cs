using System;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Weasel.Postgresql.Tables;

namespace Marten.Storage.Metadata
{
    internal class LastModifiedColumn: MetadataColumn<DateTimeOffset>, ISelectableColumn
    {
        public LastModifiedColumn() : base(SchemaConstants.LastModifiedColumn, x => x.LastModified)
        {
            DefaultExpression = "(transaction_timestamp())";
            Type = "timestamp with time zone";
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async, GeneratedMethod sync,
            int index, DocumentMapping mapping)
        {
            var variableName = "lastModified";
            var memberType = typeof(DateTimeOffset);

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
