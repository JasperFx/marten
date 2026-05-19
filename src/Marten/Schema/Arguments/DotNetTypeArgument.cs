using System;
using System.Reflection;
using JasperFx.Core.Reflection;
using NpgsqlTypes;

namespace Marten.Schema.Arguments;

internal class DotNetTypeArgument: UpsertArgument
{
    private static readonly MethodInfo _getType = typeof(object).GetMethod("GetType");

    private static readonly MethodInfo _fullName =
        ReflectionHelper.GetProperty<Type>(x => x.FullName).GetMethod;

    public DotNetTypeArgument()
    {
        Arg = "docDotNetType";
        Column = SchemaConstants.DotNetTypeColumn;
        DbType = NpgsqlDbType.Varchar;
        PostgresType = "varchar";
    }
}
