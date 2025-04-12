using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx;
using JasperFx.Core;
using JasperFx.Core.Descriptions;
using Marten.Schema;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Table = Weasel.Postgresql.Tables.Table;

namespace Marten.Storage;

public class MasterTableTenancyOptions
{
    internal readonly Dictionary<string, string> SeedDatabases = new();

    /// <summary>
    ///     The connection string of the master database holding the tenant table
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    ///     A configured data source for managing the tenancy
    /// </summary>
    public NpgsqlDataSource? DataSource { get; set; }

    /// <summary>
    ///     If specified, override the database schema name for the tenants table
    ///     Default is "public"
    /// </summary>
    public string SchemaName { get; set; } = "public";

    /// <summary>
    ///     Set an application name in the connection strings strictly for diagnostics
    /// </summary>
    public string ApplicationName { get; set; } = Assembly.GetEntryAssembly()?.FullName ?? "Marten";

    /// <summary>
    ///     If set, this will override the AutoCreate setting for just the master tenancy table
    /// </summary>
    public AutoCreate? AutoCreate { get; set; }

    /// <summary>
    ///     For the sake of testing, seed the master tenancy table with a tenant
    ///     database
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="connectionString"></param>
    public void RegisterDatabase(string tenantId, string connectionString)
    {
        SeedDatabases[tenantId] = connectionString;
    }

    internal string CorrectedConnectionString()
    {
        if (ApplicationName.IsNotEmpty())
        {
            var builder = new NpgsqlConnectionStringBuilder(ConnectionString) { ApplicationName = ApplicationName };

            return builder.ConnectionString;
        }

        return ConnectionString;
    }

    internal string CorrectConnectionString(string connectionString)
    {
        if (ApplicationName.IsNotEmpty())
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = ApplicationName };

            return builder.ConnectionString;
        }

        return connectionString;
    }
}

public class MasterTableTenancy: ITenancy, ITenancyWithMasterDatabase
{
    private readonly MasterTableTenancyOptions _configuration;
    private readonly Lazy<NpgsqlDataSource> _dataSource;
    private readonly StoreOptions _options;
    private readonly string _schemaName;
    private readonly Lazy<TenantLookupDatabase> _tenantDatabase;
    private ImHashMap<string, MartenDatabase> _databases = ImHashMap<string, MartenDatabase>.Empty;

    private bool _hasAppliedChanges;
    private bool _hasAppliedDefaults;

    public MasterTableTenancy(StoreOptions options, string connectionString, string schemaName): this(options,
        new MasterTableTenancyOptions { ConnectionString = connectionString, SchemaName = schemaName })
    {
    }

    public MasterTableTenancy(StoreOptions options, MasterTableTenancyOptions tenancyOptions)
    {
        _options = options;

        _configuration = tenancyOptions;

        if (tenancyOptions.DataSource != null)
        {
            _dataSource = new Lazy<NpgsqlDataSource>(() => tenancyOptions.DataSource);
        }
        else if (tenancyOptions.ConnectionString.IsNotEmpty())
        {
            _dataSource = new Lazy<NpgsqlDataSource>(() =>
                _options.NpgsqlDataSourceFactory.Create(tenancyOptions.ConnectionString));
        }
        else
        {
            // TODO -- remove this when we figure out how to use the DI data source
            throw new ArgumentOutOfRangeException(nameof(tenancyOptions),
                "Either an NpgsqlDataSource or ConnectionString is required at this point");
        }

        _schemaName = tenancyOptions.SchemaName;
        Cleaner = new CompositeDocumentCleaner(this, _options);

        _tenantDatabase = new Lazy<TenantLookupDatabase>(() =>
            new TenantLookupDatabase(_options, _dataSource.Value, tenancyOptions.SchemaName));
    }

    public void Dispose()
    {
        foreach (var entry in _databases.Enumerate()) entry.Value.Dispose();

        if (_dataSource.IsValueCreated)
        {
            _dataSource.Value.Dispose();
        }
    }

    public async ValueTask<IReadOnlyList<IDatabase>> BuildDatabases()
    {
        await maybeApplyChanges(_tenantDatabase.Value).ConfigureAwait(false);

        await using var conn = _dataSource.Value.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        try
        {
            if (!_hasAppliedDefaults)
            {
                await seedDatabasesAsync(conn).ConfigureAwait(false);
            }

            await using var reader = await ((DbCommand)conn
                    .CreateCommand($"select tenant_id, connection_string from {_schemaName}.{TenantTable.TableName}"))
                .ExecuteReaderAsync().ConfigureAwait(false);

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                var tenantId = await reader.GetFieldValueAsync<string>(0).ConfigureAwait(false);
                tenantId = _options.MaybeCorrectTenantId(tenantId);

                // Be idempotent, don't duplicate
                if (_databases.Contains(tenantId))
                {
                    continue;
                }

                var connectionString = await reader.GetFieldValueAsync<string>(1).ConfigureAwait(false);
                connectionString = _configuration.CorrectConnectionString(connectionString);

                var database = new MartenDatabase(_options, _options.NpgsqlDataSourceFactory.Create(connectionString),
                    tenantId);
                _databases = _databases.AddOrUpdate(tenantId, database);
            }

            await reader.CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }

        var list = _databases.Enumerate().Select(x => x.Value).OfType<IDatabase>().ToList();

        list.Insert(0, _tenantDatabase.Value);
        return list;
    }

    public Tenant Default => throw new NotSupportedException("Default tenant does not supported");
    public IDocumentCleaner Cleaner { get; }

    public Tenant GetTenant(string tenantId)
    {
        tenantId = _options.MaybeCorrectTenantId(tenantId);
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
        tenantId = _options.MaybeCorrectTenantId(tenantId);
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
        tenantIdOrDatabaseIdentifier = _options.MaybeCorrectTenantId(tenantIdOrDatabaseIdentifier);
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
        tenantId = _options.MaybeCorrectTenantId(tenantId);
        return database.Identifier == tenantId;
    }

    public PostgresqlDatabase TenantDatabase => _tenantDatabase.Value;

    public async Task DeleteDatabaseRecordAsync(string tenantId)
    {
        tenantId = _options.MaybeCorrectTenantId(tenantId);
        await maybeApplyChanges(_tenantDatabase.Value).ConfigureAwait(false);

        await _dataSource.Value
            .CreateCommand($"delete from {_schemaName}.{TenantTable.TableName} where tenant_id = :id")
            .With("id", tenantId)
            .ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task ClearAllDatabaseRecordsAsync()
    {
        await maybeApplyChanges(_tenantDatabase.Value).ConfigureAwait(false);

        await _dataSource.Value.CreateCommand($"delete from {_schemaName}.{TenantTable.TableName}")
            .ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task AddDatabaseRecordAsync(string tenantId, string connectionString)
    {
        tenantId = _options.MaybeCorrectTenantId(tenantId);
        await _dataSource.Value
            .CreateCommand(
                $"insert into {_schemaName}.{TenantTable.TableName} (tenant_id, connection_string) values (:id, :connection) on conflict (tenant_id) do update set connection_string = :connection")
            .With("id", tenantId)
            .With("connection", connectionString)
            .ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task maybeApplyChanges(TenantLookupDatabase tenantDatabase)
    {
        if (!_hasAppliedChanges && (_configuration.AutoCreate ?? _options.AutoCreateSchemaObjects) != AutoCreate.None)
        {
#pragma warning disable MA0032
            await tenantDatabase
                .ApplyAllConfiguredChangesToDatabaseAsync(_options.AutoCreateSchemaObjects).ConfigureAwait(false);
#pragma warning restore MA0032
            _hasAppliedChanges = true;
        }
    }

    private async Task seedDatabasesAsync(NpgsqlConnection conn)
    {
        if (!_configuration.SeedDatabases.Any())
        {
            return;
        }

        var builder = new BatchBuilder();
        foreach (var pair in _configuration.SeedDatabases)
        {
            builder.StartNewCommand();
            var parameters = builder.AppendWithParameters(
                $"insert into {_schemaName}.{TenantTable.TableName} (tenant_id, connection_string) values (?, ?) on conflict (tenant_id) do update set connection_string = ?");

            parameters[0].Value = pair.Key;
            parameters[1].Value = pair.Value;
            parameters[2].Value = pair.Value;
        }

        var batch = builder.Compile();
        batch.Connection = conn;
        await batch.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

        _hasAppliedDefaults = true;
    }

    private async Task<MartenDatabase?> tryFindTenantDatabase(string tenantId)
    {
        tenantId = _options.MaybeCorrectTenantId(tenantId);
        var connectionString = (string)await _dataSource.Value
            .CreateCommand($"select connection_string from {_schemaName}.{TenantTable.TableName} where tenant_id = :id")
            .With("id", tenantId)
            .ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);

        if (connectionString.IsEmpty())
        {
            return null;
        }

        connectionString = _configuration.CorrectConnectionString(connectionString);

        return connectionString.IsNotEmpty()
            ? new MartenDatabase(_options,
                _options.NpgsqlDataSourceFactory.Create(connectionString), tenantId)
            : null;
    }

    internal class TenantLookupDatabase: PostgresqlDatabase
    {
        private readonly TenantDatabaseStorage _feature;

        public TenantLookupDatabase(StoreOptions options, NpgsqlDataSource dataSource, string schemaName): base(options,
            options.AutoCreateSchemaObjects, options.Advanced.Migrator, "TenantDatabases", dataSource)
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
        private readonly StoreOptions _options;
        private readonly string _schemaName;

        public TenantDatabaseStorage(string schemaName, StoreOptions options): base("TenantDatabases",
            options.Advanced.Migrator)
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

        public TenantTable(DbObjectName name): base(name)
        {
        }

        public TenantTable(string schemaName): base(new DbObjectName(schemaName, TableName))
        {
            AddColumn<string>("tenant_id").AsPrimaryKey();
            AddColumn<string>("connection_string").NotNull();
        }
    }

    public async ValueTask<DatabaseUsage> DescribeDatabasesAsync(CancellationToken token)
    {
        // TODO -- watch out for duplicate databases!!!
        await BuildDatabases().ConfigureAwait(false);
        var list = _databases.Enumerate().Select(pair =>
        {
            var descriptor = pair.Value.Describe();
            descriptor.TenantIds.Add(pair.Key);
            return descriptor;
        }).ToList();

        return new DatabaseUsage
        {
            Cardinality = DatabaseCardinality.DynamicMultiple,
            Databases = list
        };
    }

    public DatabaseCardinality Cardinality => DatabaseCardinality.DynamicMultiple;
}
