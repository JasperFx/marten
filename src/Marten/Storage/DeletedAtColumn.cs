using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage
{
    internal class DeletedAtColumn: MetadataColumn, ISelectableColumn
    {
        public DeletedAtColumn() : base(DocumentMapping.DeletedAtColumn, "timestamp with time zone")
        {
            CanAdd = true;
            Directive = "NULL";
        }

        public override async Task ApplyAsync(DocumentMetadata metadata, int index, DbDataReader reader, CancellationToken token)
        {
            metadata.DeletedAt = await reader.GetFieldValueAsync<DateTime>(index, token);
        }

        public override void Apply(DocumentMetadata metadata, int index, DbDataReader reader)
        {
            metadata.DeletedAt = reader.GetFieldValue<DateTime>(index);
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async, GeneratedMethod sync,
            int index, DocumentMapping mapping)
        {
            var member = mapping.SoftDeletedAtMember;
            var variableName = "deletedAt";
            var memberType = typeof(DateTime);

            if (member == null) return;

            sync.Frames.Code($"if (!reader.IsDBNull({index}))");
            sync.Frames.Code("{{");

            async.Frames.CodeAsync($"if (!(await reader.IsDBNullAsync({index}, token)))");
            async.Frames.Code("{{");

            sync.Frames.Code($"var {variableName} = reader.GetFieldValue<{memberType.FullNameInCode()}>({index});");
            async.Frames.CodeAsync($"var {variableName} = await reader.GetFieldValueAsync<{memberType.FullNameInCode()}>({index}, token);");

            sync.Frames.SetMemberValue(member, variableName, mapping.DocumentType, generatedType);
            async.Frames.SetMemberValue(member, variableName, mapping.DocumentType, generatedType);

            sync.Frames.Code("}}");
            async.Frames.Code("}}");
        }

        public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
        {
            return mapping.SoftDeletedAtMember != null;
        }
    }
}
