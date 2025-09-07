using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx;
using JasperFx.Descriptors;
using JasperFx.MultiTenancy;
using Marten.Schema;
using Npgsql;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Connections;
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
    ISingleServerMultiTenancy, ITenancyWithMasterDatabase
{
    private readonly Lazy<NpgsqlDataSource> _masterDataSource;
    private readonly StoreOptions _options;

    private readonly Dictionary<string, string> _tenantToDatabase = new();

    private Tenant _default;
    private string[] _lastTenantIds;

    private PostgresqlDatabase _tenantDatabase;

    private ImHashMap<string, Tenant> _tenants = ImHashMap<string, Tenant>.Empty;

    public SingleServerMultiTenancy(
        INpgsqlDataSourceFactory dataSourceFactory,
        string masterConnectionString,
        StoreOptions options
    ): base(dataSourceFactory, masterConnectionString)
    {
        _options = options;
        Cleaner = new CompositeDocumentCleaner(this, options);

        _masterDataSource =
            new Lazy<NpgsqlDataSource>(() => options.NpgsqlDataSourceFactory.Create(masterConnectionString));
    }

    public DatabaseCardinality Cardinality => DatabaseCardinality.DynamicMultiple;

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

    public void Dispose()
    {
        foreach (var entry in _tenants.Enumerate()) entry.Value.Database.Dispose();

        _default?.Database?.Dispose();
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

    public async ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier)
    {
        var tenant = await GetTenantAsync(_options.TenantIdStyle.MaybeCorrectTenantId(tenantIdOrDatabaseIdentifier))
            .ConfigureAwait(false);
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

    public PostgresqlDatabase TenantDatabase
    {
        get
        {
            _tenantDatabase ??= new MasterDatabase(_masterDataSource.Value);
            return _tenantDatabase;
        }
    }

    public async ValueTask<DatabaseUsage> DescribeDatabasesAsync(CancellationToken token)
    {
        await BuildDatabases().ConfigureAwait(false);

        // Tracking the databases against Marten's identifier
        var dict = new Dictionary<string, DatabaseDescriptor>();
        foreach (var db in AllDatabases()) dict[db.Identifier] = db.Describe();

        var usage = new DatabaseUsage
        {
            Cardinality = DatabaseCardinality.DynamicMultiple, Databases = dict.Values.ToList()
        };

        foreach (var pair in _tenantToDatabase) dict[pair.Value].TenantIds.Add(pair.Key);

        return usage;
    }

    protected override MartenDatabase buildDatabase(string databaseName, NpgsqlDataSource npgsqlDataSource)
    {
        return new MartenDatabase(_options, npgsqlDataSource, databaseName);
    }

    internal class MasterDatabase: PostgresqlDatabase
    {
        public MasterDatabase(NpgsqlDataSource dataSource): base(new DefaultMigrationLogger(), AutoCreate.None,
            new PostgresqlMigrator(), "MartenMaster", dataSource)
        {
        }

        public override IFeatureSchema[] BuildFeatureSchemas()
        {
            return Array.Empty<IFeatureSchema>();
        }
    }
}
