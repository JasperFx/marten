using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Schema;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Connections;
using Weasel.Postgresql.Tables;
using CommandExtensions = Weasel.Core.CommandExtensions;

namespace Marten.Storage;

public class MasterTableTenancy : ITenancy
{
    private readonly StoreOptions _options;
    private readonly string _connectionString;
    private readonly string _schemaName;
    private ImHashMap<string, MartenDatabase> _databases = ImHashMap<string, MartenDatabase>.Empty;

    public MasterTableTenancy(StoreOptions options, string connectionString, string schemaName)
    {
        _options = options;
        _connectionString = connectionString;
        _schemaName = schemaName;
        Cleaner = new CompositeDocumentCleaner(this);

    }

    public void Dispose()
    {
        foreach (var entry in _databases.Enumerate())
        {
            entry.Value.Dispose();
        }
    }

    public async Task DeleteDatabaseRecordAsync(string tenantId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await CommandExtensions.CreateCommand(conn, $"delete from {_schemaName}.{TenantTable.TableName} where tenant_id = :id")
            .With("id", tenantId)
            .ExecuteOnce(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task ClearAllDatabaseRecordsAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await CommandExtensions.CreateCommand(conn, $"delete from {_schemaName}.{TenantTable.TableName}")
            .ExecuteOnce(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task AddDatabaseRecordAsync(string tenantId, string connectionString)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.CreateCommand($"insert into {_schemaName}.{TenantTable.TableName} (tenant_id, connection_string) values (:id, :connection) on conflict (tenant_id) do update set connection_string = :connection")
            .With("id", tenantId)
            .With("connection", connectionString)
            .ExecuteOnce(CancellationToken.None).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<IDatabase>> BuildDatabases()
    {
        var tenantDatabase = new TenantDatabase(_options, _connectionString, _schemaName);

        if (_options.AutoCreateSchemaObjects != AutoCreate.None)
        {
            await tenantDatabase
                .ApplyAllConfiguredChangesToDatabaseAsync(_options.AutoCreateSchemaObjects).ConfigureAwait(false);
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        try
        {
            await using var reader = await ((DbCommand)conn
                .CreateCommand($"select tenant_id, connection_string from {_schemaName}.{TenantTable.TableName}")).ExecuteReaderAsync().ConfigureAwait(false);

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                var tenantId = await reader.GetFieldValueAsync<string>(0).ConfigureAwait(false);

                // Be idempotent, don't duplicate
                if (_databases.Contains(tenantId)) continue;

                var connectionString = await reader.GetFieldValueAsync<string>(1).ConfigureAwait(false);

                var database = new MartenDatabase(_options, new ConnectionFactory(new DefaultNpgsqlDataSourceFactory(), connectionString), tenantId);
                _databases = _databases.AddOrUpdate(tenantId, database);
            }
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }

        var list = _databases.Enumerate().Select(x => x.Value).OfType<IDatabase>().ToList();

        list.Insert(0, tenantDatabase);
        return list;
    }

    public Tenant Default => throw new NotSupportedException($"Default tenant does not supported");
    public IDocumentCleaner Cleaner { get; }

    private async Task<MartenDatabase?> tryFindTenantDatabase(string tenantId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(CancellationToken.None).ConfigureAwait(false);

        var connectionString = (string)await CommandExtensions.CreateCommand(conn, $"select connection_string from {_schemaName}.{TenantTable.TableName} where tenant_id = :id")
            .With("id", tenantId)
            .ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);

        await conn.CloseAsync().ConfigureAwait(false);

        return connectionString.IsNotEmpty()
            ? new MartenDatabase(_options,
                new ConnectionFactory(new DefaultNpgsqlDataSourceFactory(), connectionString), tenantId)
            : null;
    }

    public Tenant GetTenant(string tenantId)
    {
        if (_databases.TryFind(tenantId, out var database))
        {
            return new Tenant(tenantId, database);
        }

        // It's actually important to *not* do any synchronous IO in case
        // someone is using multiplexing
        database = tryFindTenantDatabase(tenantId).GetAwaiter().GetResult();
        if (database == null)
        {
            throw new UnknownTenantIdException(tenantId);
        }

        _databases = _databases.AddOrUpdate(tenantId, database);

        return new Tenant(tenantId, database);
    }

    public async ValueTask<Tenant> GetTenantAsync(string tenantId)
    {
        if (_databases.TryFind(tenantId, out var database))
        {
            return new Tenant(tenantId, database);
        }

        database = await tryFindTenantDatabase(tenantId).ConfigureAwait(false);
        if (database == null)
        {
            throw new UnknownTenantIdException(tenantId);
        }

        _databases = _databases.AddOrUpdate(tenantId, database);

        return new Tenant(tenantId, database);
    }

    public async ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier)
    {
        if (_databases.TryFind(tenantIdOrDatabaseIdentifier, out var database))
        {
            return database;
        }

        database = await tryFindTenantDatabase(tenantIdOrDatabaseIdentifier).ConfigureAwait(false);
        if (database == null)
        {
            throw new UnknownTenantIdException(tenantIdOrDatabaseIdentifier);
        }

        return database;
    }

    public bool IsTenantStoredInCurrentDatabase(IMartenDatabase database, string tenantId)
    {
        return database.Identifier == tenantId;
    }

    internal class TenantDatabase: PostgresqlDatabase
    {
        private readonly TenantDatabaseStorage _feature;

        public TenantDatabase(StoreOptions options, string connectionString, string schemaName) : base(options, options.AutoCreateSchemaObjects, options.Advanced.Migrator, "TenantDatabases", NpgsqlDataSource.Create(connectionString))
        {
            _feature = new TenantDatabaseStorage(schemaName, options);
        }

        public override IFeatureSchema[] BuildFeatureSchemas()
        {
            return [_feature];
        }
    }

    internal class TenantDatabaseStorage: FeatureSchemaBase
    {
        private readonly string _schemaName;
        private readonly StoreOptions _options;

        public TenantDatabaseStorage(string schemaName, StoreOptions options) : base("TenantDatabases", options.Advanced.Migrator)
        {
            _schemaName = schemaName;
        }

        protected override IEnumerable<ISchemaObject> schemaObjects()
        {
            yield return new TenantTable(_schemaName);
        }
    }

    internal class TenantTable: Table
    {
        public const string TableName = "mt_tenant_databases";

        public TenantTable(DbObjectName name) : base(name)
        {
        }

        public TenantTable(string schemaName) : base(new DbObjectName(schemaName, TableName))
        {
            AddColumn<string>("tenant_id").AsPrimaryKey();
            AddColumn<string>("connection_string").NotNull();
        }
    }
}


