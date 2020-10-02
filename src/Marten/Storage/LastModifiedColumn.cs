using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage
{
    internal class LastModifiedColumn: MetadataColumn, ISelectableColumn
    {
        public LastModifiedColumn() : base(DocumentMapping.LastModifiedColumn, "timestamp with time zone")
        {
            Directive = "DEFAULT transaction_timestamp()";
            CanAdd = true;
        }

        public override async Task ApplyAsync(DocumentMetadata metadata, int index, DbDataReader reader, CancellationToken token)
        {
            metadata.LastModified = await reader.GetFieldValueAsync<DateTime>(index, token);
        }

        public override void Apply(DocumentMetadata metadata, int index, DbDataReader reader)
        {
            metadata.LastModified = reader.GetFieldValue<DateTime>(index);
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async, GeneratedMethod sync,
            int index, DocumentMapping mapping)
        {
            var member = mapping.LastModifiedMember;
            var variableName = "lastModified";
            var memberType = typeof(DateTime);

            if (member == null) return;

            sync.Frames.Code($"var {variableName} = reader.GetFieldValue<{memberType.FullNameInCode()}>({index});");
            async.Frames.CodeAsync($"var {variableName} = await reader.GetFieldValueAsync<{memberType.FullNameInCode()}>({index}, token);");

            sync.Frames.SetMemberValue(member, variableName, mapping.DocumentType, generatedType);
            async.Frames.SetMemberValue(member, variableName, mapping.DocumentType, generatedType);
        }

        public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
        {
            return mapping.LastModifiedMember != null;
        }
    }
}
