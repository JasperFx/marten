using System.Linq.Expressions;
using System.Reflection;
using Baseline.Reflection;
using NpgsqlTypes;

namespace Marten.Schema.Arguments
{
    public class DocTypeArgument : UpsertArgument
    {
        private static readonly MethodInfo _getAlias = ReflectionHelper.GetMethod<DocumentMapping>(x => x.AliasFor(null));
        private static readonly MethodInfo _getType = typeof(object).GetMethod("GetType");

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

        public override Expression CompileUpdateExpression(EnumStorage enumStorage, ParameterExpression call, ParameterExpression doc, ParameterExpression updateBatch, ParameterExpression mapping, ParameterExpression currentVersion, ParameterExpression newVersion)
        {
            var argName = Expression.Constant(Arg);
            var dbType = Expression.Constant(NpgsqlDbType.Varchar);

            var type = Expression.Call(doc, _getType);
            var alias = Expression.Call(mapping, _getAlias, type);

            return Expression.Call(call, _paramMethod, argName, alias, dbType);
        }
    }
}