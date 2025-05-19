using Npgsql;
using Weasel.Core.MultiTenancy;
using Weasel.Postgresql;

namespace Marten.Storage;

public class MasterTableTenancyOptions: MasterTableTenancyOptions<NpgsqlDataSource>
{
    public MasterTableTenancyOptions(): base(PostgresqlProvider.Instance)
    {
    }

    public void RegisterDatabase(string tenantId, string connectionString)
    {
        SeedDatabases.Register(tenantId, connectionString);
    }
}
