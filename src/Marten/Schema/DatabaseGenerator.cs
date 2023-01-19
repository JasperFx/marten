using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Marten.Storage;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Schema;

public sealed class DatabaseGenerator: IDatabaseCreationExpressions
{
    private readonly Dictionary<string, TenantDatabaseCreationExpressions> _configurationPerTenant = new();
    private string _maintenanceDbConnectionString;

    public IDatabaseCreationExpressions MaintenanceDatabase(string maintenanceDbConnectionString)
    {
        _maintenanceDbConnectionString = maintenanceDbConnectionString ??
                                         throw new ArgumentNullException(nameof(maintenanceDbConnectionString));

        return this;
    }

    public ITenantDatabaseCreationExpressions ForTenant(string tenantId = Tenancy.DefaultTenantId)
    {
        var configurator = new TenantDatabaseCreationExpressions();
        _configurationPerTenant.Add(tenantId, configurator);
        return configurator;
    }

    public async Task CreateDatabasesAsync(ITenancy tenancy, Action<IDatabaseCreationExpressions> configure)
    {
        configure(this);

        foreach (var tenantConfig in _configurationPerTenant)
        {
            var tenant = await tenancy.GetTenantAsync(tenantConfig.Key).ConfigureAwait(false);
            var config = tenantConfig.Value;

            await createDbAsync(tenant, config).ConfigureAwait(false);
        }
    }

    private async Task createDbAsync(Tenant tenant, TenantDatabaseCreationExpressions config)
    {
        string catalog;
        var maintenanceDb = _maintenanceDbConnectionString;

        using (var t = tenant.Database.CreateConnection())
        {
            catalog = t.Database;

            if (maintenanceDb == null)
            {
                var cstringBuilder = new NpgsqlConnectionStringBuilder(t.ConnectionString);
                cstringBuilder.Database = "postgres";
                maintenanceDb = cstringBuilder.ToString();
            }

            var noExistingDb = config.CheckAgainstCatalog
                ? new Func<Task<bool>>(() => IsNotInPgDatabase(catalog, maintenanceDb))
                : () => cannotConnectDueToInvalidCatalog(t);

            if (await noExistingDb().ConfigureAwait(false))
            {
                await CreateDbAsync(catalog, config, false, maintenanceDb).ConfigureAwait(false);
                return;
            }
        }

        if (config.DropExistingDatabase)
        {
            await CreateDbAsync(catalog, config, true, maintenanceDb).ConfigureAwait(false);
        }
    }

    private async Task<bool> IsNotInPgDatabase(string catalog, string maintenanceDb)
    {
        var connection = new NpgsqlConnection(maintenanceDb);
        await using var _ = connection.ConfigureAwait(false);
        var cmd = connection.CreateCommand("SELECT datname FROM pg_database where datname = @catalog");
        await using var __ = cmd.ConfigureAwait(false);
        cmd.AddNamedParameter("catalog", catalog);

        await connection.OpenAsync().ConfigureAwait(false);

        try
        {
            var m = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            return m == null;
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    private static async Task<bool> cannotConnectDueToInvalidCatalog(NpgsqlConnection t)
    {
        try
        {
            await t.OpenAsync().ConfigureAwait(false);
            await t.CloseAsync().ConfigureAwait(false);
        }
        // INVALID CATALOG NAME (https://www.postgresql.org/docs/current/static/errcodes-appendix.html)
        catch (PostgresException e) when (e.SqlState == "3D000")
        {
            return true;
        }

        return false;
    }

    private async Task CreateDbAsync(string catalog, TenantDatabaseCreationExpressions config, bool dropExisting,
        string maintenanceDb)
    {
        var cmdText = string.Empty;

        if (dropExisting)
        {
            if (config.KillConnections)
            {
                cmdText =
                    $"SELECT pg_terminate_backend(pg_stat_activity.pid) FROM pg_stat_activity WHERE pg_stat_activity.datname = '{catalog}' AND pid <> pg_backend_pid();";
            }

            cmdText += $"DROP DATABASE IF EXISTS \"{catalog}\";";
        }

        var connection = new NpgsqlConnection(maintenanceDb);
        await using var _ = connection.ConfigureAwait(false);
        var cmd = connection.CreateCommand(cmdText);
        await using var __ = cmd.ConfigureAwait(false);
        cmd.CommandText += $"CREATE DATABASE \"{catalog}\" WITH" + config;
        await connection.OpenAsync().ConfigureAwait(false);
        try
        {
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            config.OnDbCreated?.Invoke(connection);
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    private sealed class TenantDatabaseCreationExpressions: ITenantDatabaseCreationExpressions
    {
        private readonly StringBuilder _createOptions = new();
        public bool DropExistingDatabase { get; private set; }
        public bool CheckAgainstCatalog { get; private set; }
        public bool KillConnections { get; private set; }
        public Action<NpgsqlConnection> OnDbCreated { get; private set; }

        public ITenantDatabaseCreationExpressions DropExisting(bool killConnections = false)
        {
            DropExistingDatabase = true;
            KillConnections = killConnections;
            return this;
        }

        public ITenantDatabaseCreationExpressions WithEncoding(string encoding)
        {
            _createOptions.Append($" ENCODING = '{encoding}'");
            return this;
        }

        public ITenantDatabaseCreationExpressions WithOwner(string owner)
        {
            _createOptions.Append($" OWNER = '{owner}'");
            return this;
        }

        public ITenantDatabaseCreationExpressions ConnectionLimit(int limit)
        {
            _createOptions.Append($" CONNECTION LIMIT = {limit}");
            return this;
        }

        public ITenantDatabaseCreationExpressions LcCollate(string lcCollate)
        {
            _createOptions.Append($" LC_COLLATE = '{lcCollate}'");
            return this;
        }

        public ITenantDatabaseCreationExpressions LcType(string lcType)
        {
            _createOptions.Append($" LC_CTYPE = '{lcType}'");
            return this;
        }

        public ITenantDatabaseCreationExpressions TableSpace(string tableSpace)
        {
            _createOptions.Append($" TABLESPACE  = {tableSpace}");
            return this;
        }

        public ITenantDatabaseCreationExpressions CheckAgainstPgDatabase()
        {
            CheckAgainstCatalog = true;
            return this;
        }

        public ITenantDatabaseCreationExpressions OnDatabaseCreated(Action<NpgsqlConnection> onDbCreated)
        {
            OnDbCreated = onDbCreated;
            return this;
        }

        public override string ToString()
        {
            return _createOptions.ToString();
        }
    }
}
