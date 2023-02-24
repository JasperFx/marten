#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Schema;
using Weasel.Core.Migrations;

namespace Marten.Storage;

internal class DefaultTenancy: Tenancy, ITenancy
{
    private readonly IConnectionFactory _factory;
    private readonly StoreOptions _options;


    public DefaultTenancy(IConnectionFactory factory, StoreOptions options): base(options)
    {
        _factory = factory;
    }

    public Tenant GetTenant(string tenantId)
    {
        return new Tenant(tenantId, Default.Database);
    }

    public Tenant Default { get; private set; }

    public IDocumentCleaner Cleaner => Default.Database;

    public ValueTask<Tenant> GetTenantAsync(string tenantId)
    {
        return new ValueTask<Tenant>(GetTenant(tenantId));
    }

    public ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier)
    {
        return new ValueTask<IMartenDatabase>(Default.Database);
    }

    public bool IsTenantStoredInCurrentDatabase(IMartenDatabase database, string tenantId)
    {
        return true;
    }

    public ValueTask<IReadOnlyList<IDatabase>> BuildDatabases()
    {
        return new ValueTask<IReadOnlyList<IDatabase>>(new IDatabase[] { Default.Database });
    }

    internal void Initialize()
    {
        Default = new Tenant(DefaultTenantId, new MartenDatabase(Options, _factory, Options.StoreName));
    }
}
