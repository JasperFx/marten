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

/// <summary>
/// 32-bit (integer) revision argument for the IRevisioned column variant (#4614). The
/// codegen UpsertFunction path uses this to declare the `revision` parameter as
/// `integer` in `mt_upsert_*` functions whose target column is `integer`, so Postgres
/// can bind without an implicit-cast mismatch.
/// </summary>
internal class RevisionArgumentInt32: UpsertArgument
{
    public RevisionArgumentInt32()
    {
        Arg = "revision";
        PostgresType = "integer";
        DbType = NpgsqlDbType.Integer;
        Column = SchemaConstants.VersionColumn;
    }
}
