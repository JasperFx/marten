using System;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Schema.Identity;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class VersionArgument : UpsertArgument
    {
        private readonly static MethodInfo _newGuid =
            typeof(CombGuidIdGeneration).GetMethod(nameof(CombGuidIdGeneration.New),
                BindingFlags.Static | BindingFlags.Public);

        public VersionArgument()
        {
            Arg = "docVersion";
            Column = DocumentMapping.VersionColumn;
            DbType = NpgsqlDbType.Uuid;
            PostgresType = "uuid";
        }

        public override Expression CompileBulkImporter(EnumStorage enumStorage, Expression writer, ParameterExpression document)
        {
            var value = Expression.Call(_newGuid);
            var dbType = Expression.Constant(DbType);

            var method = writeMethod.MakeGenericMethod(typeof(Guid));

            return Expression.Call(writer, method, Expression.Convert(value, typeof(object)), dbType);
        }

        public override Expression CompileUpdateExpression(EnumStorage enumStorage, ParameterExpression call, ParameterExpression doc,
            ParameterExpression json, ParameterExpression mapping, ParameterExpression typeAlias)
        {
            var value = Expression.Call(_newGuid);

            var dbType = Expression.Constant(DbType);
            return Expression.Call(call, _paramMethod, Expression.Constant(Arg), Expression.Convert(value, typeof(object)), dbType);
        }
    }
}