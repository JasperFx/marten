using System;
using System.Linq.Expressions;
using System.Reflection;
using NpgsqlTypes;

namespace Marten.Schema.Arguments
{
    public class VersionArgument: UpsertArgument
    {
        public const string ArgName = "docVersion";

        public VersionArgument()
        {
            Arg = ArgName;
            Column = DocumentMapping.VersionColumn;
            DbType = NpgsqlDbType.Uuid;
            PostgresType = "uuid";
        }

        public override Expression CompileBulkImporter(DocumentMapping mapping, EnumStorage enumStorage, Expression writer, ParameterExpression document, ParameterExpression alias, ParameterExpression serializer, ParameterExpression textWriter, ParameterExpression version, ParameterExpression tenantId)
        {

            var dbType = Expression.Constant(DbType);
            var method = writeMethod.MakeGenericMethod(typeof(Guid));

            return Expression.Call(writer, method, version, dbType);
        }

        public override Expression CompileUpdateExpression(EnumStorage enumStorage, ParameterExpression call, ParameterExpression doc, ParameterExpression updateBatch, ParameterExpression mapping, ParameterExpression currentVersion, ParameterExpression newVersion, ParameterExpression tenantId, bool useCharBufferPooling)
        {
            var dbType = Expression.Constant(DbType);
            return Expression.Call(call, _paramMethod, Expression.Constant(Arg), Expression.Convert(newVersion, typeof(object)), dbType);
        }
    }
}
