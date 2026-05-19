using NpgsqlTypes;

namespace Marten.Schema.Arguments;

internal class DocJsonBodyArgument: UpsertArgument
{
    public DocJsonBodyArgument()
    {
        Arg = "doc";
        PostgresType = "JSONB";
        DbType = NpgsqlDbType.Jsonb;
        Column = "data";
    }
}
