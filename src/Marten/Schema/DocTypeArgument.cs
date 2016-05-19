using System.Linq.Expressions;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class DocTypeArgument : UpsertArgument
    {
        public DocTypeArgument()
        {
            Arg = "docType";
            Column = DocumentMapping.DocumentTypeColumn;
            DbType = NpgsqlDbType.Varchar;
            PostgresType = "varchar";
        }

        public override Expression CompileBulkImporter(EnumStorage enumStorage, Expression writer, ParameterExpression document, ParameterExpression alias, ParameterExpression serializer)
        {
            var method = writeMethod.MakeGenericMethod(typeof(string));
            var dbType = Expression.Constant(DbType);

            return Expression.Call(writer, method, alias, dbType);
        }

        public override Expression CompileUpdateExpression(EnumStorage enumStorage, ParameterExpression call, ParameterExpression doc,
            ParameterExpression json, ParameterExpression mapping, ParameterExpression typeAlias)
        {
            var argName = Expression.Constant(Arg);
            return Expression.Call(call, _paramMethod, argName, typeAlias, Expression.Constant(NpgsqlDbType.Varchar));
        }
    }
}