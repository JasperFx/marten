using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Schema;
using Weasel.Core.Migrations;

#nullable enable
namespace Marten.Storage
{
    internal class DefaultTenancy: Tenancy, ITenancy
    {
        public DefaultTenancy(IConnectionFactory factory, StoreOptions options): base(options)
        {
            Default = new Tenant(DefaultTenantId, new MartenDatabase(options, factory, DefaultTenantId));
        }

        public Tenant GetTenant(string tenantId)
        {
            return new Tenant(tenantId, Default.Database);
        }

        public Tenant Default { get; }

        public IDocumentCleaner Cleaner => Default.Database;
        public ValueTask<Tenant> GetTenantAsync(string tenantId)
        {
            return new ValueTask<Tenant>(GetTenant(tenantId));
        }

        public ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier)
        {
            return new ValueTask<IMartenDatabase>(Default.Database);
        }

        public ValueTask<IReadOnlyList<IDatabase>> BuildDatabases()
        {
            return new ValueTask<IReadOnlyList<IDatabase>>(new IDatabase[] { Default.Database });
        }
    }
}
