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
            AllowNulls = true;
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async, GeneratedMethod sync,
            int index, DocumentMapping mapping)
        {
            setMemberFromReader(generatedType, async, sync, index, mapping);
        }

        public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
        {
            return Member != null;
        }
    }
}
