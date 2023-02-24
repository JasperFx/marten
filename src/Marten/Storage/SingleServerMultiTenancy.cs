using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Schema;
using Weasel.Core.Migrations;
using Weasel.Postgresql.Migrations;

namespace Marten.Storage;

public interface ISingleServerMultiTenancy
{
    /// <summary>
    ///     Use to seed tenant database names for Marten database management
    /// </summary>
    /// <param name="tenantIds"></param>
    /// <returns></returns>
    ISingleServerMultiTenancy WithTenants(params string[] tenantIds);

    /// <summary>
    ///     Add the previous tenantIds to the named database
    /// </summary>
    /// <param name="databaseName"></param>
    /// <returns></returns>
    ISingleServerMultiTenancy InDatabaseNamed(string databaseName);
}

internal class SingleServerMultiTenancy: SingleServerDatabaseCollection<MartenDatabase>, ITenancy,
    ISingleServerMultiTenancy
{
    private readonly StoreOptions _options;

    private readonly Dictionary<string, string> _tenantToDatabase = new();

    private Tenant _default;
    private string[] _lastTenantIds;

    private ImHashMap<string, Tenant> _tenants = ImHashMap<string, Tenant>.Empty;

    public SingleServerMultiTenancy(string masterConnectionString, StoreOptions options): base(masterConnectionString)
    {
        _options = options;
        Cleaner = new CompositeDocumentCleaner(this);
    }

    public ISingleServerMultiTenancy WithTenants(params string[] tenantIds)
    {
        _lastTenantIds = tenantIds;

        foreach (var tenantId in tenantIds) _tenantToDatabase[tenantId] = tenantId;
        return this;
    }

    public ISingleServerMultiTenancy InDatabaseNamed(string databaseName)
    {
        foreach (var tenantId in _lastTenantIds) _tenantToDatabase[tenantId] = databaseName;

        return this;
    }

    public Tenant GetTenant(string tenantId)
    {
        if (_tenants.TryFind(tenantId, out var tenant))
        {
            return tenant;
        }

        if (!_tenantToDatabase.TryGetValue(tenantId, out var databaseName))
        {
            databaseName = tenantId;
        }

        var database = FindOrCreateDatabase(databaseName).AsTask()
            .GetAwaiter().GetResult();

        tenant = new Tenant(tenantId, database);
        _tenants = _tenants.AddOrUpdate(tenantId, tenant);

        return tenant;
    }

    public bool IsTenantStoredInCurrentDatabase(IMartenDatabase database, string tenantId)
    {
        var tenant = GetTenant(tenantId);
        return ReferenceEquals(database, tenant.Database);
    }

    public async ValueTask<Tenant> GetTenantAsync(string tenantId)
    {
        if (_tenants.TryFind(tenantId, out var tenant))
        {
            return tenant;
        }

        if (!_tenantToDatabase.TryGetValue(tenantId, out var databaseName))
        {
            databaseName = tenantId;
        }

        var database = await base.FindOrCreateDatabase(databaseName).ConfigureAwait(false);
        tenant = new Tenant(tenantId, database);
        _tenants = _tenants.AddOrUpdate(tenantId, tenant);

        return tenant;
    }

    public new async ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier)
    {
        var tenant = await GetTenantAsync(tenantIdOrDatabaseIdentifier).ConfigureAwait(false);
        return tenant.Database;
    }

    public Tenant Default
    {
        get
        {
            _default = _default
                ??= _tenants.Enumerate().Select(x => x.Value).FirstOrDefault();

            return _default;
        }
    }

    public IDocumentCleaner Cleaner { get; }

    public async ValueTask<IReadOnlyList<IDatabase>> BuildDatabases()
    {
        // This just guarantees that all databases are built
        foreach (var tenantId in _tenantToDatabase.Values.Distinct())
            await FindOrCreateDatabase(tenantId).ConfigureAwait(false);

        return AllDatabases();
    }

    protected override MartenDatabase buildDatabase(string databaseName, string connectionString)
    {
        return new MartenDatabase(_options, new ConnectionFactory(connectionString), databaseName);
    }
}
