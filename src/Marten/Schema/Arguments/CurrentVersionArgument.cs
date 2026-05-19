using NpgsqlTypes;

namespace Marten.Schema.Arguments;

internal class CurrentVersionArgument: UpsertArgument
{
    public CurrentVersionArgument()
    {
        Arg = "current_version";
        PostgresType = "uuid";
        DbType = NpgsqlDbType.Uuid;
        Column = null;
    }
}
