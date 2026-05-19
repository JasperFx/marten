using System.Reflection;
using JasperFx.Core.Reflection;
using NpgsqlTypes;

namespace Marten.Schema.Arguments;

internal class DocTypeArgument: UpsertArgument
{
    private static readonly MethodInfo _getAlias = ReflectionHelper.GetMethod<DocumentMapping>(x => x.AliasFor(null));
    private static readonly MethodInfo _getType = typeof(object).GetMethod("GetType")!;

    public DocTypeArgument()
    {
        Arg = "docType";
        Column = SchemaConstants.DocumentTypeColumn;
        DbType = NpgsqlDbType.Varchar;
        PostgresType = "varchar";
    }
}
