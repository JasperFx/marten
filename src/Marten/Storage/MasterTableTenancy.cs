using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Descriptors;
using JasperFx.MultiTenancy;
using Marten.Schema;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Core.MultiTenancy;
using Weasel.Postgresql;
using Weasel.Postgresql.MultiTenancy;
using Table = Weasel.Postgresql.Tables.Table;

namespace Marten.Storage;

/// <summary>
///     Database-per-tenant multi-tenancy driven by the <c>mt_tenant_databases</c> control table. The
///     control-table runtime (the tenant cache, lookups, the add/disable/enable/remove lifecycle and
///     <see cref="IDynamicTenantSource{T}" />) is Weasel's
///     <see cref="MasterTableTenancyBase{TDatabase,TDataSource}" /> (#5733); this class supplies Marten's
///     databases, its tenant id style, its migration of the control table and its <see cref="ITenancy" />.
/// </summary>
public class MasterTableTenancy: MasterTableTenancyBase<MartenDatabase, NpgsqlDataSource>, ITenancy,
    ITenancyWithMasterDatabase
{
    private readonly StoreOptions _options;
    private readonly Lazy<TenantLookupDatabase> _tenantDatabase;
    private readonly SemaphoreSlim _applyingChanges = new(1, 1);

    private volatile bool _hasAppliedChanges;

    public MasterTableTenancy(StoreOptions options, string connectionString, string schemaName): this(options,
        new MasterTableTenancyOptions() { ConnectionString = connectionString, SchemaName = schemaName })
    {
    }

    public MasterTableTenancy(StoreOptions options, MasterTableTenancyOptions tenancyOptions)
        : base(tenancyOptions, TenantTable.TableName, PostgresqlMasterTenantTableDialect.Instance)
    {
        _options = options;

        Cleaner = new CompositeDocumentCleaner(this, _options);

        _tenantDatabase = new Lazy<TenantLookupDatabase>(() =>
            new TenantLookupDatabase(_options, MasterDataSource, tenancyOptions.SchemaName));
    }

    /// <summary>
    ///     The control-plane data source is built from the configured connection string as given, as
    ///     it always has been: the application name is applied to the tenant databases' connections.
    /// </summary>
    protected override NpgsqlDataSource BuildDataSource(string connectionString)
        => _options.NpgsqlDataSourceFactory.Create(Options.ConnectionString!);

    protected override MartenDatabase BuildDatabase(string tenantId, string connectionString)
    {
        var database = new MartenDatabase(_options, _options.NpgsqlDataSourceFactory.Create(connectionString),
            tenantId);
        database.TenantIds.Fill(tenantId);
        return database;
    }

    protected override string CorrectTenantId(string tenantId)
        => _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);

    /// <summary>
    ///     #5479: a disabled tenant is a tenant this store knows about and is refusing on purpose, so it
    ///     is refused with <see cref="DisabledTenantException" />, which derives from
    ///     <see cref="UnknownTenantIdException" /> (jasperfx#882) so an existing catch keeps catching it.
    /// </summary>
    protected override UnknownTenantIdException CreateTenantRefusal(string tenantId, MasterTenantStatus status)
        => status == MasterTenantStatus.Disabled
            ? new DisabledTenantException(tenantId)
            : new UnknownTenantIdException(tenantId);

    /// <summary>
    ///     <see cref="MasterTableTenancyOptions{T}.AutoCreate" /> overrides the store's setting "for just
    ///     the master tenancy table", so it chooses the mode the table is migrated under.
    /// </summary>
    protected override AutoCreate EffectiveAutoCreate => Options.AutoCreate ?? _options.AutoCreateSchemaObjects;

    /// <summary>
    ///     The control table is migrated through Marten's own Weasel migration of
    ///     <see cref="TenantLookupDatabase" /> rather than the dialect's create-if-missing DDL, so a table
    ///     an older Marten created is brought up to date (the <c>disabled</c> column, #4607).
    /// </summary>
    protected override async Task ProvisionControlTableAsync(CancellationToken token)
    {
        if (_hasAppliedChanges)
        {
            return;
        }

        var autoCreate = EffectiveAutoCreate;
        if (autoCreate == AutoCreate.None)
        {
            return;
        }

        await _applyingChanges.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_hasAppliedChanges)
            {
                return;
            }

#pragma warning disable MA0032
            await _tenantDatabase.Value
                .ApplyAllConfiguredChangesToDatabaseAsync(autoCreate, ct: token).ConfigureAwait(false);
#pragma warning restore MA0032
            _hasAppliedChanges = true;
        }
        finally
        {
            _applyingChanges.Release();
        }
    }

    /// <summary>
    ///     #6058 (weasel#850), the cold-process half of #5985: lets a synchronous <c>GetTenant()</c> in a process where
    ///     nothing asynchronous has run yet resolve the tenant synchronously, instead of blocking a thread on the
    ///     asynchronous lookup. Makes the control table readable with blocking I/O and the same idempotent DDL the
    ///     dialect creates it with, plus the <c>disabled</c> column an older Marten's table lacks (#4607). Declines
    ///     while an asynchronous provisioning holds the lock, rather than waiting on it.
    /// </summary>
    /// <remarks>
    ///     It does not stand in for <see cref="ProvisionControlTableAsync" />: the first asynchronous entry point
    ///     still runs the Weasel migration of <see cref="TenantLookupDatabase" />.
    /// </remarks>
    protected override bool TryProvisionControlTableSynchronously()
    {
        // The operator owns the schema, as the asynchronous path assumes too
        var autoCreate = EffectiveAutoCreate;
        if (_hasAppliedChanges || autoCreate == AutoCreate.None)
        {
            return true;
        }

        // Never block on the semaphore: the asynchronous provisioning holds it across an await
        if (!_applyingChanges.Wait(0))
        {
            return false;
        }

        try
        {
            if (_hasAppliedChanges)
            {
                return true;
            }

            var sql = Dialect.CreateControlTable(Options.SchemaName, TableName, QualifiedTableName);
            if (autoCreate != AutoCreate.CreateOnly)
            {
                sql += $"{Environment.NewLine}alter table {QualifiedTableName} add column if not exists " +
                       $"{TenantTable.DisabledColumn} boolean default false not null;";
            }

            using var conn = MasterDataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();

            return true;
        }
        finally
        {
            _applyingChanges.Release();
        }
    }

    public async ValueTask<IReadOnlyList<IDatabase>> BuildDatabases()
    {
        var databases = await BuildDatabasesAsync(CancellationToken.None).ConfigureAwait(false);

        var list = databases.OfType<IDatabase>().ToList();
        list.Insert(0, _tenantDatabase.Value);
        return list;
    }

    // #5479. Was "Default tenant does not supported". The sharded twin
    // (ShardedTenancy.cs) already had the sentence this one should have had.
    public Tenant Default => throw new NotSupportedException(
        "The default tenant is not supported with master-table multi-tenancy. All operations require a tenant id.");

    public IDocumentCleaner Cleaner { get; }

    public Tenant GetTenant(string tenantId)
    {
        tenantId = CorrectTenantId(tenantId);
        var database = FindCachedDatabase(tenantId);
        if (database != null)
        {
            return new Tenant(tenantId, database);
        }

        // #5985: resolve the uncached tenant with synchronous I/O on this thread (weasel#838). Blocking a pool
        // thread on the asynchronous lookup starved the pool on a burst of first sessions for new tenants: the
        // lookup needed a free pool thread to finish, and every caller was holding one. Weasel declines -- and
        // this falls back to the asynchronous lookup, as before -- when it cannot read without waiting on async
        // work: nothing has provisioned the control table in this process yet, or the control database is
        // multiplexed, which Npgsql only supports asynchronously
        if (!TryResolveTenant(tenantId, out var resolution))
        {
            resolution = ResolveTenantAsync(tenantId, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }

        database = resolution.Database ?? throw CreateTenantRefusal(tenantId, resolution.Status);
        return new Tenant(tenantId, database);
    }

    public async ValueTask<Tenant> GetTenantAsync(string tenantId)
    {
        tenantId = CorrectTenantId(tenantId);
        var database = await resolveOrThrowAsync(tenantId).ConfigureAwait(false);
        return new Tenant(tenantId, database);
    }

    public async ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier)
        => await resolveOrThrowAsync(tenantIdOrDatabaseIdentifier).ConfigureAwait(false);

    /// <summary>
    /// #5400 — the null-returning form of <see cref="FindOrCreateDatabase"/>. This tenancy never
    /// provisions a tenant on a lookup (it is a single select against the tenant table), so this only
    /// swaps an unknown tenant from a throw to a null. A disabled tenant is still refused with
    /// <see cref="DisabledTenantException" />: it is not a tenant that does not exist.
    /// </summary>
    public async ValueTask<IMartenDatabase?> TryFindDatabase(string tenantIdOrDatabaseIdentifier)
    {
        var resolution = await ResolveTenantAsync(tenantIdOrDatabaseIdentifier, CancellationToken.None)
            .ConfigureAwait(false);

        return resolution.Status switch
        {
            MasterTenantStatus.Disabled => throw CreateTenantRefusal(
                CorrectTenantId(tenantIdOrDatabaseIdentifier), MasterTenantStatus.Disabled),
            _ => resolution.Database
        };
    }

    public async ValueTask<IMartenDatabase> FindDatabase(DatabaseId id)
    {
        // Not worried about this being optimized at all
        var database = AllDatabases().FirstOrDefault(x => x.Id == id);
        if (database != null) return database;

        // Try to refresh once
        await BuildDatabases().ConfigureAwait(false);

        database = AllDatabases().FirstOrDefault(x => x.Id == id);

        if (database == null)
        {
            throw new ArgumentOutOfRangeException(nameof(id), $"Requested database {id.Identity} cannot be found");
        }

        return database;
    }

    public bool IsTenantStoredInCurrentDatabase(IMartenDatabase database, string tenantId)
    {
        tenantId = CorrectTenantId(tenantId);
        return database.Identifier == tenantId;
    }

    public PostgresqlDatabase TenantDatabase => _tenantDatabase.Value;

    /// <summary>
    ///     Record a tenant's connection string and cache its database eagerly. The
    ///     <see cref="IDynamicTenantSource{T}" /> spelling of
    ///     <see cref="MasterTableTenancyBase{TDatabase,TDataSource}.AddDatabaseRecordAsync" />.
    /// </summary>
    public Task AddTenantAsync(string tenantId, string connectionValue)
        => AddDatabaseRecordAsync(tenantId, connectionValue, CancellationToken.None);

    /// <summary>
    ///     Evict a tenant and delete its record. The tenant's own database is left alone. The
    ///     <see cref="IDynamicTenantSource{T}" /> spelling of
    ///     <see cref="MasterTableTenancyBase{TDatabase,TDataSource}.DeleteDatabaseRecordAsync" />.
    /// </summary>
    public Task RemoveTenantAsync(string tenantId)
        => DeleteDatabaseRecordAsync(tenantId, CancellationToken.None);

    private async ValueTask<MartenDatabase> resolveOrThrowAsync(string tenantId)
    {
        var resolution = await ResolveTenantAsync(tenantId, CancellationToken.None).ConfigureAwait(false);
        return resolution.Database ?? throw CreateTenantRefusal(CorrectTenantId(tenantId), resolution.Status);
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
            AddColumn<string>(StorageConstants.TenantIdColumn).AsPrimaryKey();
            AddColumn<string>(StorageConstants.ConnectionStringColumn).NotNull();
            AddColumn<bool>(DisabledColumn).DefaultValueByExpression("false").NotNull();
        }

        public const string DisabledColumn = "disabled";
    }

    public async ValueTask<DatabaseUsage> DescribeDatabasesAsync(CancellationToken token)
    {
        await BuildDatabases().ConfigureAwait(false);
        var list = new List<DatabaseDescriptor>();
        foreach (var tenantId in AllTenantIds())
        {
            var database = FindCachedDatabase(tenantId);
            if (database == null) continue;

            var descriptor = database.Describe();
            descriptor.TenantIds.Fill(tenantId);
            list.Add(descriptor);
        }

        return new DatabaseUsage
        {
            Cardinality = DatabaseCardinality.DynamicMultiple,
            Databases = list
        };
    }
}
