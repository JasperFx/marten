using System;
using System.Reflection;
using NpgsqlTypes;

namespace Marten.Schema.Arguments;

internal class VersionArgument: UpsertArgument
{
    public const string ArgName = "docVersion";

    private static readonly MethodInfo _newGuid =
        typeof(Guid).GetMethod(nameof(Guid.NewGuid),
            BindingFlags.Static | BindingFlags.Public)!;

    public VersionArgument()
    {
        Arg = ArgName;
        Column = SchemaConstants.VersionColumn;
        DbType = NpgsqlDbType.Uuid;
        PostgresType = "uuid";
    }
}
