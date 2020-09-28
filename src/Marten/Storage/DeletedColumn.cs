using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;

namespace Marten.Storage
{
    internal class DeletedColumn: MetadataColumn
    {
        public DeletedColumn() : base(DocumentMapping.DeletedColumn, "boolean")
        {
            Directive = "DEFAULT FALSE";
            CanAdd = true;
        }

        public override async Task ApplyAsync(DocumentMetadata metadata, int index, DbDataReader reader, CancellationToken token)
        {
            metadata.Deleted = await reader.GetFieldValueAsync<bool>(index, token);
        }

        public override void Apply(DocumentMetadata metadata, int index, DbDataReader reader)
        {
            metadata.Deleted = reader.GetFieldValue<bool>(index);
        }
    }
}
