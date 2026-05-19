using NpgsqlTypes;

namespace Marten.Schema.Arguments;

internal class RevisionArgument: UpsertArgument
{
    public RevisionArgument()
    {
        Arg = "revision";
        PostgresType = "bigint";
        DbType = NpgsqlDbType.Bigint;
        Column = SchemaConstants.VersionColumn;
    }
}
