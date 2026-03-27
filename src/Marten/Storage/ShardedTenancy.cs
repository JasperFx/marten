using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx;
using JasperFx.Core;
using JasperFx.Descriptors;
using JasperFx.MultiTenancy;
using Marten.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Core.MultiTenancy;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Weasel.Postgresql.Tables.Partitioning;
using Table = Weasel.Postgresql.Tables.Table;

namespace Marten.Storage;

/// <summary>
/// Multi-tenancy implementation that distributes tenants across a pool of databases
/// with conjoined tenancy and native PG list partitioning per tenant within each database.
/// </summary>
public class ShardedTenancy : ITenancy, ITenancyWithMasterDatabase, ITenantDatabasePool
{
    private readonly StoreOptions _options;
    private readonly ShardedTenancyOptions _configuration;
    private readonly Lazy<NpgsqlDataSource> _dataSource;
    private readonly Lazy<PoolLookupDatabase> _poolDatabase;
    private readonly string _schemaName;

    // tenant_id -> (MartenDatabase, databaseId)
    private ImHashMap<string, MartenDatabase> _tenantToDatabase = ImHashMap<string, MartenDatabase>.Empty;
    // database_id -> MartenDatabase
    private ImHashMap<string, MartenDatabase> _databasesById = ImHashMap<string, MartenDatabase>.Empty;

    private bool _hasAppliedChanges;
    private bool _hasSeeded;

    // Advisory lock key for tenant assignment serialization
    private const int AdvisoryLockKey = 4173_0001; // Unique to sharded tenancy

    public ShardedTenancy(StoreOptions options, ShardedTenancyOptions configuration)
    {
        _options = options;
        _configuration = configuration;
        _schemaName = configuration.SchemaName;

        if (configuration.DataSource != null)
        {
            _dataSource = new Lazy<NpgsqlDataSource>(() => configuration.DataSource);
        }
        else if (configuration.ConnectionString.IsNotEmpty())
        {
            _dataSource = new Lazy<NpgsqlDataSource>(() =>
                _options.NpgsqlDataSourceFactory.Create(configuration.ConnectionString));
        }
        else
        {
            throw new ArgumentException(
                "Either a ConnectionString or DataSource must be provided for ShardedTenancy");
        }

        Cleaner = new CompositeDocumentCleaner(this, _options);

        _poolDatabase = new Lazy<PoolLookupDatabase>(() =>
            new PoolLookupDatabase(_options, _dataSource.Value, _schemaName));
    }

    public void Dispose()
    {
        foreach (var entry in _databasesById.Enumerate()) entry.Value.Dispose();

        if (_dataSource.IsValueCreated)
        {
            _dataSource.Value.Dispose();
        }
    }

    #region ITenancy

    public Tenant Default => throw new NotSupportedException(
        "Default tenant is not supported with sharded multi-tenancy. All operations require a tenant ID.");

    public IDocumentCleaner Cleaner { get; }

    public Tenant GetTenant(string tenantId)
    {
        tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        if (_tenantToDatabase.TryFind(tenantId, out var database))
        {
            return new Tenant(tenantId, database);
        }

        database = findOrAssignTenantDatabaseAsync(tenantId).GetAwaiter().GetResult();
        return new Tenant(tenantId, database);
    }

    public async ValueTask<Tenant> GetTenantAsync(string tenantId)
    {
        tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        if (_tenantToDatabase.TryFind(tenantId, out var database))
        {
            return new Tenant(tenantId, database);
        }

        database = await findOrAssignTenantDatabaseAsync(tenantId).ConfigureAwait(false);
        return new Tenant(tenantId, database);
    }

    public async ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier)
    {
        tenantIdOrDatabaseIdentifier = _options.TenantIdStyle.MaybeCorrectTenantId(tenantIdOrDatabaseIdentifier);

        // Try tenant lookup first
        if (_tenantToDatabase.TryFind(tenantIdOrDatabaseIdentifier, out var database))
        {
            return database;
        }

        // Try database id lookup
        if (_databasesById.TryFind(tenantIdOrDatabaseIdentifier, out database))
        {
            return database;
        }

        return await findOrAssignTenantDatabaseAsync(tenantIdOrDatabaseIdentifier).ConfigureAwait(false);
    }

    public async ValueTask<IMartenDatabase> FindDatabase(DatabaseId id)
    {
        var database = _databasesById.Enumerate().Select(x => x.Value).FirstOrDefault(x => x.Id == id);
        if (database != null) return database;

        await BuildDatabases().ConfigureAwait(false);

        database = _databasesById.Enumerate().Select(x => x.Value).FirstOrDefault(x => x.Id == id);
        if (database == null)
        {
            throw new ArgumentOutOfRangeException(nameof(id), $"Requested database {id.Identity} cannot be found");
        }

        return database;
    }

    public bool IsTenantStoredInCurrentDatabase(IMartenDatabase database, string tenantId)
    {
        tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        if (_tenantToDatabase.TryFind(tenantId, out var assignedDb))
        {
            return assignedDb.Id == database.Id;
        }
        return false;
    }

    public async ValueTask<IReadOnlyList<IDatabase>> BuildDatabases()
    {
        await maybeApplyChanges().ConfigureAwait(false);
        await maybeSeedDatabases().ConfigureAwait(false);

        await using var conn = _dataSource.Value.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        try
        {
            // Load all databases from pool
            await using var poolReader = await ((DbCommand)conn
                    .CreateCommand($"select database_id, connection_string, is_full, tenant_count from {_schemaName}.{DatabasePoolTable.TableName}"))
                .ExecuteReaderAsync().ConfigureAwait(false);

            while (await poolReader.ReadAsync().ConfigureAwait(false))
            {
                var databaseId = await poolReader.GetFieldValueAsync<string>(0).ConfigureAwait(false);

                if (_databasesById.TryFind(databaseId, out _)) continue;

                var connectionString = await poolReader.GetFieldValueAsync<string>(1).ConfigureAwait(false);
                connectionString = _configuration.CorrectedConnectionString(connectionString);

                var database = new MartenDatabase(_options,
                    _options.NpgsqlDataSourceFactory.Create(connectionString), databaseId);

                _databasesById = _databasesById.AddOrUpdate(databaseId, database);
            }

            await poolReader.CloseAsync().ConfigureAwait(false);

            // Load all tenant assignments
            await using var assignReader = await ((DbCommand)conn
                    .CreateCommand($"select tenant_id, database_id from {_schemaName}.{TenantAssignmentTable.TableName}"))
                .ExecuteReaderAsync().ConfigureAwait(false);

            while (await assignReader.ReadAsync().ConfigureAwait(false))
            {
                var tenantId = await assignReader.GetFieldValueAsync<string>(0).ConfigureAwait(false);
                tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
                var databaseId = await assignReader.GetFieldValueAsync<string>(1).ConfigureAwait(false);

                if (_databasesById.TryFind(databaseId, out var database))
                {
                    database.TenantIds.Fill(tenantId);
                    _tenantToDatabase = _tenantToDatabase.AddOrUpdate(tenantId, database);
                }
            }

            await assignReader.CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }

        var list = _databasesById.Enumerate().Select(x => x.Value).OfType<IDatabase>().ToList();
        list.Insert(0, _poolDatabase.Value);
        return list;
    }

    #endregion

    #region ITenancyWithMasterDatabase

    public PostgresqlDatabase TenantDatabase => _poolDatabase.Value;

    #endregion

    #region ITenantDatabasePool

    public async ValueTask<IReadOnlyList<PooledDatabase>> ListDatabasesAsync(CancellationToken ct)
    {
        await maybeApplyChanges().ConfigureAwait(false);
        await maybeSeedDatabases().ConfigureAwait(false);

        var list = new List<PooledDatabase>();

        await using var conn = _dataSource.Value.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var reader = await ((DbCommand)conn
                .CreateCommand($"select database_id, connection_string, is_full, tenant_count from {_schemaName}.{DatabasePoolTable.TableName}"))
            .ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new PooledDatabase(
                await reader.GetFieldValueAsync<string>(0, ct).ConfigureAwait(false),
                await reader.GetFieldValueAsync<string>(1, ct).ConfigureAwait(false),
                await reader.GetFieldValueAsync<bool>(2, ct).ConfigureAwait(false),
                await reader.GetFieldValueAsync<int>(3, ct).ConfigureAwait(false)
            ));
        }

        await conn.CloseAsync().ConfigureAwait(false);
        return list;
    }

    public async ValueTask AddDatabaseAsync(string databaseId, string connectionString, CancellationToken ct)
    {
        await maybeApplyChanges().ConfigureAwait(false);

        await _dataSource.Value
            .CreateCommand(
                $"insert into {_schemaName}.{DatabasePoolTable.TableName} (database_id, connection_string) values (:id, :conn) on conflict (database_id) do update set connection_string = :conn")
            .With("id", databaseId)
            .With("conn", connectionString)
            .ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        // Create and cache the MartenDatabase
        var corrected = _configuration.CorrectedConnectionString(connectionString);
        var database = new MartenDatabase(_options,
            _options.NpgsqlDataSourceFactory.Create(corrected), databaseId);
        _databasesById = _databasesById.AddOrUpdate(databaseId, database);
    }

    public async ValueTask MarkDatabaseFullAsync(string databaseId, CancellationToken ct)
    {
        await maybeApplyChanges().ConfigureAwait(false);

        await _dataSource.Value
            .CreateCommand(
                $"update {_schemaName}.{DatabasePoolTable.TableName} set is_full = true where database_id = :id")
            .With("id", databaseId)
            .ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask<string?> FindDatabaseForTenantAsync(string tenantId, CancellationToken ct)
    {
        tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        await maybeApplyChanges().ConfigureAwait(false);

        var result = await _dataSource.Value
            .CreateCommand(
                $"select database_id from {_schemaName}.{TenantAssignmentTable.TableName} where tenant_id = :id")
            .With("id", tenantId)
            .ExecuteScalarAsync(ct).ConfigureAwait(false);

        return result as string;
    }

    public async ValueTask AssignTenantAsync(string tenantId, string databaseId, CancellationToken ct)
    {
        tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        await maybeApplyChanges().ConfigureAwait(false);
        await maybeSeedDatabases().ConfigureAwait(false);

        await using var conn = _dataSource.Value.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // Acquire advisory lock
        await conn.CreateCommand($"select pg_advisory_lock({AdvisoryLockKey})")
            .ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        try
        {
            await conn.CreateCommand(
                    $"insert into {_schemaName}.{TenantAssignmentTable.TableName} (tenant_id, database_id) values (:tid, :did) on conflict (tenant_id) do update set database_id = :did")
                .With("tid", tenantId)
                .With("did", databaseId)
                .ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await conn.CreateCommand(
                    $"update {_schemaName}.{DatabasePoolTable.TableName} set tenant_count = (select count(*) from {_schemaName}.{TenantAssignmentTable.TableName} where database_id = :did) where database_id = :did")
                .With("did", databaseId)
                .ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            // Create partition in the target database
            if (_databasesById.TryFind(databaseId, out var database))
            {
                database.TenantIds.Fill(tenantId);
                _tenantToDatabase = _tenantToDatabase.AddOrUpdate(tenantId, database);

                await createPartitionsForTenant(database, tenantId, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            await conn.CreateCommand($"select pg_advisory_unlock({AdvisoryLockKey})")
                .ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask RemoveTenantAsync(string tenantId, CancellationToken ct)
    {
        tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        await maybeApplyChanges().ConfigureAwait(false);

        await _dataSource.Value
            .CreateCommand(
                $"delete from {_schemaName}.{TenantAssignmentTable.TableName} where tenant_id = :id")
            .With("id", tenantId)
            .ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    #endregion

#pragma warning disable MA0032
    #region Internals

    private async Task<MartenDatabase> findOrAssignTenantDatabaseAsync(string tenantId)
    {
        await maybeApplyChanges().ConfigureAwait(false);
        await maybeSeedDatabases().ConfigureAwait(false);

        // Step 1: Check assignment table
        var databaseId = await FindDatabaseForTenantAsync(tenantId, CancellationToken.None)
            .ConfigureAwait(false);

        if (databaseId != null && _databasesById.TryFind(databaseId, out var database))
        {
            database.TenantIds.Fill(tenantId);
            _tenantToDatabase = _tenantToDatabase.AddOrUpdate(tenantId, database);
            return database;
        }

        // Step 2: Auto-assign under advisory lock
        await using var conn = _dataSource.Value.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await conn.CreateCommand($"select pg_advisory_lock({AdvisoryLockKey})")
            .ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            // Double-check after acquiring lock
            var existingDbId = (string?)await conn
                .CreateCommand(
                    $"select database_id from {_schemaName}.{TenantAssignmentTable.TableName} where tenant_id = :id")
                .With("id", tenantId)
                .ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);

            if (existingDbId != null && _databasesById.TryFind(existingDbId, out database))
            {
                database.TenantIds.Fill(tenantId);
                _tenantToDatabase = _tenantToDatabase.AddOrUpdate(tenantId, database);
                return database;
            }

            // Get available databases
            var availableDatabases = new List<PooledDatabase>();
            await using var reader = await ((DbCommand)conn
                    .CreateCommand(
                        $"select database_id, connection_string, is_full, tenant_count from {_schemaName}.{DatabasePoolTable.TableName} where is_full = false"))
                .ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                availableDatabases.Add(new PooledDatabase(
                    await reader.GetFieldValueAsync<string>(0).ConfigureAwait(false),
                    await reader.GetFieldValueAsync<string>(1).ConfigureAwait(false),
                    false,
                    await reader.GetFieldValueAsync<int>(3).ConfigureAwait(false)
                ));
            }

            await reader.CloseAsync().ConfigureAwait(false);

            // Run assignment strategy
            var assignedDbId = await _configuration.AssignmentStrategy
                .AssignTenantToDatabaseAsync(tenantId, availableDatabases).ConfigureAwait(false);

            // Write assignment
            await conn.CreateCommand(
                    $"insert into {_schemaName}.{TenantAssignmentTable.TableName} (tenant_id, database_id) values (:tid, :did) on conflict (tenant_id) do update set database_id = :did")
                .With("tid", tenantId)
                .With("did", assignedDbId)
                .ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

            // Update tenant count
            await conn.CreateCommand(
                    $"update {_schemaName}.{DatabasePoolTable.TableName} set tenant_count = tenant_count + 1 where database_id = :did")
                .With("did", assignedDbId)
                .ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

            // Ensure database is in cache
            if (!_databasesById.TryFind(assignedDbId, out database))
            {
                // Need to build it from the pool
                await BuildDatabases().ConfigureAwait(false);
                if (!_databasesById.TryFind(assignedDbId, out database))
                {
                    throw new InvalidOperationException(
                        $"Database '{assignedDbId}' was assigned but could not be found in the pool");
                }
            }

            database.TenantIds.Fill(tenantId);
            _tenantToDatabase = _tenantToDatabase.AddOrUpdate(tenantId, database);

            // Create partitions in the target database
            await createPartitionsForTenant(database, tenantId, CancellationToken.None).ConfigureAwait(false);

            return database;
        }
        finally
        {
            await conn.CreateCommand($"select pg_advisory_unlock({AdvisoryLockKey})")
                .ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    private async Task createPartitionsForTenant(MartenDatabase database, string tenantId, CancellationToken ct)
    {
        if (_options.TenantPartitions == null) return;

        var partitions = _options.TenantPartitions.Partitions;
        var dict = new Dictionary<string, string> { { tenantId, tenantId } };

        await partitions.AddPartitionToAllTables(
            NullLogger.Instance, database, dict, ct).ConfigureAwait(false);
    }

    private async Task maybeApplyChanges()
    {
        if (!_hasAppliedChanges &&
            (_configuration.AutoCreate ?? _options.AutoCreateSchemaObjects) != AutoCreate.None)
        {
            await _poolDatabase.Value
                .ApplyAllConfiguredChangesToDatabaseAsync(_options.AutoCreateSchemaObjects)
                .ConfigureAwait(false);
            _hasAppliedChanges = true;
        }
    }

    private async Task maybeSeedDatabases()
    {
        if (_hasSeeded || _configuration.SeedDatabases.Count == 0) return;

        foreach (var (databaseId, connectionString) in _configuration.SeedDatabases)
        {
            await AddDatabaseAsync(databaseId, connectionString, CancellationToken.None)
                .ConfigureAwait(false);
        }

        _hasSeeded = true;
    }

#pragma warning restore MA0032
    #endregion

    #region Descriptors

    public DatabaseCardinality Cardinality => DatabaseCardinality.DynamicMultiple;

    public async ValueTask<DatabaseUsage> DescribeDatabasesAsync(CancellationToken token)
    {
        await BuildDatabases().ConfigureAwait(false);

        var list = _databasesById.Enumerate().Select(pair =>
        {
            var descriptor = pair.Value.Describe();
            descriptor.TenantIds.AddRange(pair.Value.TenantIds);
            return descriptor;
        }).ToList();

        return new DatabaseUsage
        {
            Cardinality = DatabaseCardinality.DynamicMultiple,
            Databases = list
        };
    }

    #endregion

    #region Inner classes

    internal class PoolLookupDatabase : PostgresqlDatabase
    {
        private readonly PoolFeatureSchema _feature;

        public PoolLookupDatabase(StoreOptions options, NpgsqlDataSource dataSource, string schemaName)
            : base(options, options.AutoCreateSchemaObjects, options.Advanced.Migrator,
                "ShardedTenancyPool", dataSource)
        {
            _feature = new PoolFeatureSchema(schemaName, options);
        }

        public override IFeatureSchema[] BuildFeatureSchemas()
        {
            return [_feature];
        }
    }

    internal class PoolFeatureSchema : FeatureSchemaBase
    {
        private readonly string _schemaName;

        public PoolFeatureSchema(string schemaName, StoreOptions options)
            : base("ShardedTenancyPool", options.Advanced.Migrator)
        {
            _schemaName = schemaName;
        }

        protected override IEnumerable<ISchemaObject> schemaObjects()
        {
            yield return new DatabasePoolTable(_schemaName);
            yield return new TenantAssignmentTable(_schemaName);
        }
    }

    #endregion
}
