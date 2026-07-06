#nullable enable
using Marten.Schema.Arguments;
using Marten.Storage.Metadata;

namespace Marten.Internal.Storage;

// Lived in IDocumentStorage.cs before the interfaces moved to Weasel.Storage (#4821);
// Postgres-typed (PG ICommandBuilder + TenantIdColumn), so it stays Marten-side.
internal static class DocumentStoreExtensions
{
    public static void AddTenancyFilter(this IDocumentStorage storage, ICommandBuilder sql, string tenantId)
    {
        if (storage.TenancyStyle == TenancyStyle.Conjoined)
        {
            sql.Append(" and ");
            sql.Append("d.");
            sql.Append(TenantIdColumn.Name);
            sql.Append(" = ");
            sql.AppendParameter(tenantId);
        }
    }
}
