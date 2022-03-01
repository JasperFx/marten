using System;
using System.Threading.Tasks;
using Marten.Schema;
using Weasel.Core.Migrations;

namespace Marten.Storage
{
    public interface ITenancy : IDatabaseSource
    {
        Tenant GetTenant(string tenantId);
        Tenant Default { get; }
        IDocumentCleaner Cleaner { get; }

        ValueTask<Tenant> GetTenantAsync(string tenantId);

        ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier);
    }

    public class UnknownTenantIdException: Exception
    {
        public UnknownTenantIdException(string tenantId) : base($"Unknown tenant id '{tenantId}'")
        {
        }
    }
}
