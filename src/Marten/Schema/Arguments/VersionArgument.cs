using System;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Schema.Identity;
using NpgsqlTypes;

namespace Marten.Schema.Arguments
{
    public class VersionArgument : UpsertArgument
    {
        private readonly static MethodInfo _newGuid =
            typeof(Guid).GetMethod(nameof(Guid.NewGuid),
                BindingFlags.Static | BindingFlags.Public);

        public VersionArgument()
        {
            Arg = "docVersion";
            Column = DocumentMapping.VersionColumn;
            DbType = NpgsqlDbType.Uuid;
            PostgresType = "uuid";
        }

        public override Expression CompileBulkImporter(EnumStorage enumStorage, Expression writer, ParameterExpression document, ParameterExpression alias, ParameterExpression serializer, bool useCharBufferPooling)
        {
            Expression value = Expression.Call(_newGuid);

            var dbType = Expression.Constant(DbType);

            var method = writeMethod.MakeGenericMethod(typeof(Guid));

            return Expression.Call(writer, method, value, dbType);
        }

        public override Expression CompileUpdateExpression(EnumStorage enumStorage, ParameterExpression call, ParameterExpression doc, ParameterExpression updateBatch, ParameterExpression mapping, ParameterExpression currentVersion, ParameterExpression newVersion, bool useCharBufferPooling)
        {
            var dbType = Expression.Constant(DbType);
            return Expression.Call(call, _paramMethod, Expression.Constant(Arg), Expression.Convert(newVersion, typeof(object)), dbType);
        }
    }
}