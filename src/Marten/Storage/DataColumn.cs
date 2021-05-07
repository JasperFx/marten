using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Weasel.Postgresql.Tables;

namespace Marten.Storage
{
    internal class DataColumn: TableColumn, ISelectableColumn
    {
        public DataColumn() : base("data", "JSONB")
        {
            AllowNulls = false;
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async,
            GeneratedMethod sync, int index,
            DocumentMapping mapping)
        {
            sync.Frames.DeserializeDocument(mapping, index);
            async.Frames.DeserializeDocumentAsync(mapping, index);
        }

        public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
        {
            return true;
        }
    }
}
