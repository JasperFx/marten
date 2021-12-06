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
            Default = new Tenant(DefaultTenantId, new MartenDatabase(options, factory, DefaultTenantId));
        }

        public Tenant GetTenant(string tenantId)
        {
            return new Tenant(tenantId, Default.Storage);
        }

        public Tenant Default { get; }

        public IDocumentCleaner Cleaner => Default.Storage;

        public IReadOnlyList<IDatabase> BuildDatabases()
        {
            return new IDatabase[] { Default.Storage };
        }
    }
}
