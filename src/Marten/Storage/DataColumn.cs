using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage
{
    internal class DataColumn: TableColumn, ISelectableColumn
    {
        public DataColumn() : base("data", "JSONB", "NOT NULL")
        {
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async,
            GeneratedMethod sync, int index,
            DocumentMapping mapping)
        {
            sync.Frames.Deserialize(mapping, index);
            async.Frames.DeserializeAsync(mapping, index);
        }

        public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
        {
            return true;
        }
    }
}
