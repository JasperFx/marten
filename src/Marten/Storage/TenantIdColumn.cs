using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Storage
{
    internal class TenantIdColumn: MetadataColumn
    {
        public static new readonly string Name = "tenant_id";

        public TenantIdColumn() : base(Name, "varchar")
        {
            CanAdd = true;
            Directive = $"DEFAULT '{Tenancy.DefaultTenantId}'";
        }

        public override async Task ApplyAsync(DocumentMetadata metadata, int index, DbDataReader reader, CancellationToken token)
        {
            metadata.TenantId = await reader.GetFieldValueAsync<string>(index, token);
        }

        public override void Apply(DocumentMetadata metadata, int index, DbDataReader reader)
        {
            metadata.TenantId = reader.GetFieldValue<string>(index);
        }
    }
}
