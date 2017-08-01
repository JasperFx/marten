using System;
using System.Linq.Expressions;
using System.Reflection;
using Baseline.Reflection;
using NpgsqlTypes;

namespace Marten.Schema.Arguments
{
    public class DotNetTypeArgument : UpsertArgument
    {
        private static readonly MethodInfo _getType = typeof(object).GetMethod("GetType");

        private static readonly MethodInfo _fullName =
            ReflectionHelper.GetProperty<Type>(x => x.FullName).GetMethod;


        public DotNetTypeArgument()
        {
            Arg = "docDotNetType";
            Column = DocumentMapping.DotNetTypeColumn;
            DbType = NpgsqlDbType.Varchar;
            PostgresType = "varchar";
        }

        public override Expression CompileBulkImporter(EnumStorage enumStorage, Expression writer, ParameterExpression document, ParameterExpression alias, ParameterExpression serializer, ParameterExpression textWriter, ParameterExpression tenantId)
        {
            var getType = Expression.Call(document, _getType);
            var getName = Expression.Call(getType, _fullName);

            var method = writeMethod.MakeGenericMethod(typeof(string));

            var dbType = Expression.Constant(DbType);

            return Expression.Call(writer, method, getName, dbType);
        }

        public override Expression CompileUpdateExpression(EnumStorage enumStorage, ParameterExpression call, ParameterExpression doc, ParameterExpression updateBatch, ParameterExpression mapping, ParameterExpression currentVersion, ParameterExpression newVersion, ParameterExpression tenantId, bool useCharBufferPooling)
        {
            var getType = Expression.Call(doc, _getType);
            var getName = Expression.Call(getType, _fullName);

            var argName = Expression.Constant(Arg);
            var dbType = Expression.Constant(DbType);

            return Expression.Call(call, _paramMethod, argName, getName, dbType);
        }
    }
}