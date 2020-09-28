using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;

namespace Marten.Storage
{
    internal class DeletedAtColumn: MetadataColumn
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
    }
}
