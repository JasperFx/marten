using Marten.Storage.Metadata;
using NpgsqlTypes;

namespace Marten.Schema.Arguments;

public class TenantIdArgument: UpsertArgument
{
    public const string ArgName = "tenantid";
    private const string TenantIdFieldName = "_tenantId";

    public TenantIdArgument()
    {
        Arg = ArgName;
        PostgresType = "varchar";
        DbType = NpgsqlDbType.Varchar;
        Column = TenantIdColumn.Name;
    }
}
