using System.Collections.Generic;
using Marten.Schema;
using Weasel.Core.Migrations;

#nullable enable
namespace Marten.Storage
{
    internal class DefaultTenancy: Tenancy, ITenancy
    {
        public DefaultTenancy(IConnectionFactory factory, StoreOptions options): base(options)
        {
            Default = new Tenant(DefaultTenantId, new MartenDatabase(options, factory));
        }

        public Tenant GetTenant(string tenantId)
        {
            return new Tenant(tenantId, Default.Database);
        }

        public Tenant Default { get; }

        public IDocumentCleaner Cleaner => Default.Database;

        public IReadOnlyList<IDatabase> BuildDatabases()
        {
            return new IDatabase[] { Default.Database };
        }
    }
}
