#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Descriptors;
using Marten.Schema;
using Npgsql;
using Weasel.Core.Migrations;

namespace Marten.Storage;

internal class DefaultTenancy: Tenancy, ITenancy
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly StoreOptions _options;


    public DefaultTenancy(NpgsqlDataSource dataSource, StoreOptions options): base(options)
    {
        _dataSource = dataSource;
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
        var martenDatabase = new MartenDatabase(Options, _dataSource, Options.StoreName);
        if (Options.TenantPartitions != null)
        {
            martenDatabase.AddInitializer(Options.TenantPartitions.Partitions);
        }

        Default = new Tenant(StorageConstants.DefaultTenantId, martenDatabase);
    }

    public void Dispose()
    {
        Default.Database.Dispose();
    }

    public DatabaseCardinality Cardinality => DatabaseCardinality.Single;

    public ValueTask<DatabaseUsage> DescribeDatabasesAsync(CancellationToken token)
    {
        var usage = new DatabaseUsage
        {
            Cardinality = DatabaseCardinality.Single, MainDatabase = Default.Database.Describe()
        };

        return new ValueTask<DatabaseUsage>(usage);
    }
}
