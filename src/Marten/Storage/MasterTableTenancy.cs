using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Schema;
using Marten.Storage.Encryption;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

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
    ///     The encryption key used to encrypt/decrypt connection strings. Must be exactly 32 bytes.
    /// </summary>
    public EncryptionOptions ConnectionStringEncryptionOpts
    {
        get;
        set;
    } = new();

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
    private readonly IConnectionStringEncryptor _encryptionProvider;

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
            new TenantLookupDatabase(_options, _dataSource.Value, tenancyOptions.SchemaName, _configuration.ConnectionStringEncryptionOpts));

        _encryptionProvider = _configuration.ConnectionStringEncryptionOpts.Type switch
        {
            ConnectionStringEncryption.AES when _configuration.ConnectionStringEncryptionOpts.Key != null => new AesConnectionStringEncryptor(_configuration.ConnectionStringEncryptionOpts.Key),
            ConnectionStringEncryption.PgCrypto when _configuration.ConnectionStringEncryptionOpts.Key != null => new PgCryptoConnectionStringEncryptor(_configuration.ConnectionStringEncryptionOpts.Key),
            _ => new NoopConnectionStringEncryptor()
        };

        // Provider prerequisites will be checked in BuildDatabases
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
        await _encryptionProvider.EnsurePrerequisitesAsync(_dataSource.Value, _schemaName).ConfigureAwait(false);

        await maybeApplyChanges(_tenantDatabase.Value).ConfigureAwait(false);

        await using var conn = _dataSource.Value.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        try
        {
            if (!_hasAppliedDefaults)
            {
                await seedDatabasesAsync(conn).ConfigureAwait(false);
            }

            var command = conn.CreateCommand();
            var (sql, parameters) = _encryptionProvider.GetSelectSql(_schemaName, TenantTable.TableName, "*");
            command.CommandText = ConvertToPgPositionalParams(sql);
            foreach (var param in parameters)
            {
                command.Parameters.Add(new NpgsqlParameter { Value = param });
            }

            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
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
                connectionString = _encryptionProvider?.Decrypt(connectionString) ?? connectionString;
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
#pragma warning disable MA0032
        await maybeApplyChanges(_tenantDatabase.Value).ConfigureAwait(false);
#pragma warning restore MA0032

        var cmd = _dataSource.Value.CreateCommand($"delete from {_schemaName}.{TenantTable.TableName} where tenant_id = @tenant_id");
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task ClearAllDatabaseRecordsAsync()
    {
#pragma warning disable MA0032
        await maybeApplyChanges(_tenantDatabase.Value).ConfigureAwait(false);
#pragma warning restore MA0032

        var cmd = _dataSource.Value.CreateCommand($"delete from {_schemaName}.{TenantTable.TableName}");
        await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task AddDatabaseRecordAsync(string tenantId, string connectionString)
    {
        tenantId = _options.MaybeCorrectTenantId(tenantId);

        var (sql, parameters) = _encryptionProvider.GetInsertSql(_schemaName, TenantTable.TableName, tenantId, connectionString);
        var cmd = _dataSource.Value.CreateCommand(ConvertToPgPositionalParams(sql));
        foreach (var param in parameters)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = param });
        }
#pragma warning disable MA0032
        await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
#pragma warning restore MA0032
    }

    private async Task maybeApplyChanges(TenantLookupDatabase tenantDatabase, CancellationToken token = default)
    {
        if (!_hasAppliedChanges && (_configuration.AutoCreate ?? _options.AutoCreateSchemaObjects) != AutoCreate.None)
        {
            await tenantDatabase
                .ApplyAllConfiguredChangesToDatabaseAsync(_options.AutoCreateSchemaObjects, ct: default).ConfigureAwait(false);
            _hasAppliedChanges = true;
        }
    }

    private async Task seedDatabasesAsync(NpgsqlConnection conn, CancellationToken token = default)
    {
        if (!_configuration.SeedDatabases.Any())
        {
            return;
        }

        var builder = new BatchBuilder();
        foreach (var pair in _configuration.SeedDatabases)
        {
            builder.StartNewCommand();
            var (sql, parameters) = _encryptionProvider.GetInsertSql(_schemaName, TenantTable.TableName, pair.Key, pair.Value);
            var builderParams =  builder.AppendWithParameters(sql);
            for (var i = 0; i < builderParams.Length; i++)
            {
                builderParams[i].Value = parameters[i];
            }
        }

        var batch = builder.Compile();
        batch.Connection = conn;
        await batch.ExecuteNonQueryAsync(token).ConfigureAwait(false);

        _hasAppliedDefaults = true;
    }

    private async Task<MartenDatabase?> tryFindTenantDatabase(string tenantId)
    {
        tenantId = _options.MaybeCorrectTenantId(tenantId);

        var (sql, parameters) = _encryptionProvider.GetSelectSql(_schemaName, TenantTable.TableName, tenantId);
        var cmd = _dataSource.Value.CreateCommand(ConvertToPgPositionalParams(sql));
        foreach (var param in parameters)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = param });
        }

        await using var reader = await cmd.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
        var connectionString = string.Empty;
        while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            connectionString = await reader.GetFieldValueAsync<string>(1, CancellationToken.None).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                connectionString = _encryptionProvider?.Decrypt(connectionString) ?? connectionString;
                connectionString = _configuration.CorrectConnectionString(connectionString);
            }
        }

        await reader.CloseAsync().ConfigureAwait(false);

        return connectionString.IsNotEmpty()
            ? new MartenDatabase(_options,
                _options.NpgsqlDataSourceFactory.Create(connectionString), tenantId)
            : null;
    }

    private string ConvertToPgPositionalParams(string sql)
    {
        var index = 0;
#pragma warning disable MA0009
        return Regex.Replace(sql, @"\?", _ => $"${++index}");
#pragma warning restore MA0009
    }


    internal class TenantLookupDatabase: PostgresqlDatabase
    {
        private readonly TenantDatabaseStorage _feature;

        public TenantLookupDatabase(StoreOptions options, NpgsqlDataSource dataSource, string schemaName, EncryptionOptions encryptionOpts): base(options,
            options.AutoCreateSchemaObjects, options.Advanced.Migrator, "TenantDatabases", dataSource)
        {
            _feature = new TenantDatabaseStorage(schemaName, options, encryptionOpts);
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
        private readonly EncryptionOptions _encryptionOpts;

        public TenantDatabaseStorage(string schemaName, StoreOptions options,EncryptionOptions encryptionOpts): base("TenantDatabases",
            options.Advanced.Migrator)
        {
            _schemaName = schemaName;
            _encryptionOpts = encryptionOpts;
        }

        protected override IEnumerable<ISchemaObject> schemaObjects()
        {
            yield return new TenantTable(_schemaName);

            if (_encryptionOpts.Type == ConnectionStringEncryption.PgCrypto)
            {
                yield return new Extension("pgcrypto");
            }
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
}
