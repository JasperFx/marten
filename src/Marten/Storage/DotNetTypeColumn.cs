using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage
{
    internal class DotNetTypeColumn: MetadataColumn
    {
        public DotNetTypeColumn() : base(DocumentMapping.DotNetTypeColumn, "varchar")
        {
            CanAdd = true;
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
