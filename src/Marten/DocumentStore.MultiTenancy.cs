using System.Threading.Tasks;
using Marten.Storage;
using Weasel.Core.MultiTenancy;

namespace Marten;

public partial class DocumentStore : IMasterTableMultiTenancy
{
    async Task<bool> IMasterTableMultiTenancy.TryAddTenantDatabaseRecordsAsync(string tenantId, string connectionString)
    {
        var tenancy = Options.Tenancy as MasterTableTenancy;
        if (tenancy is null)
        {
            return false;
        }

        await tenancy.AddDatabaseRecordAsync(tenantId, connectionString).ConfigureAwait(false);
        return true;
    }

    async Task<bool> IMasterTableMultiTenancy.ClearAllDatabaseRecordsAsync()
    {
        var tenancy = Options.Tenancy as MasterTableTenancy;
        if (tenancy is null)
        {
            return false;
        }

        await tenancy.ClearAllDatabaseRecordsAsync().ConfigureAwait(false);
        return true;
    }
}
