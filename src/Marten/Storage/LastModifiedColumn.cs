using System;
using System.Data.Common;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage
{
    internal class LastModifiedColumn: MetadataColumn<DateTimeOffset>, ISelectableColumn
    {
        public LastModifiedColumn() : base(SchemaConstants.LastModifiedColumn, x => x.LastModified)
        {
            Directive = "DEFAULT transaction_timestamp()";
            CanAdd = true;
            Type = "timestamp with time zone";
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async, GeneratedMethod sync,
            int index, DocumentMapping mapping)
        {
            var variableName = "lastModified";
            var memberType = typeof(DateTime);

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
