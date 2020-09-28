using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage
{
    internal class DotNetTypeColumn: MetadataColumn, ISelectableColumn
    {
        public DotNetTypeColumn() : base(DocumentMapping.DotNetTypeColumn, "varchar")
        {
            CanAdd = true;
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedMethod async, GeneratedMethod sync, int index,
            DocumentMapping mapping)
        {
            // Nothing
        }

        public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
        {
            return false;
        }

        public override async Task ApplyAsync(DocumentMetadata metadata, int index, DbDataReader reader, CancellationToken token)
        {
            metadata.DotNetType = await reader.GetFieldValueAsync<string>(index, token);
        }

        public override void Apply(DocumentMetadata metadata, int index, DbDataReader reader)
        {
            metadata.DotNetType = reader.GetFieldValue<string>(index);
        }
    }
}
