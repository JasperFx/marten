using NpgsqlTypes;

namespace Marten.Schema.Arguments;

internal class ExpectedVersionArgument: UpsertArgument
{
    public ExpectedVersionArgument(NpgsqlDbType dbType)
    {
        Arg = "expected_version";
        Column = SchemaConstants.ExpectedVersionColumn;
        DbType = dbType;
        PostgresType = dbType == NpgsqlDbType.Bigint ? "bigint" : "uuid";
    }
}
