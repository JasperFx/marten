using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;

namespace Marten.Storage
{
    internal class LastModifiedColumn: MetadataColumn
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
    }
}
