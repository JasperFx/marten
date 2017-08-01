using System.Linq.Expressions;
using Marten.Storage;
using NpgsqlTypes;

namespace Marten.Schema.Arguments
{
    public class TenantIdArgument : UpsertArgument
    {
        public const string ArgName = "tenantid";


        public TenantIdArgument()
        {
            Arg = ArgName;
            PostgresType = "varchar";
            DbType = NpgsqlDbType.Varchar;
            Column = TenantIdColumn.Name;
        }

        public override Expression CompileBulkImporter(EnumStorage enumStorage, Expression writer, ParameterExpression document,
            ParameterExpression alias, ParameterExpression serializer, ParameterExpression textWriter,
            ParameterExpression tenantId)
        {
            var method = writeMethod.MakeGenericMethod(typeof(string));
            var dbType = Expression.Constant(DbType);

            return Expression.Call(writer, method, tenantId, dbType);
        }

        public override Expression CompileUpdateExpression(EnumStorage enumStorage, ParameterExpression call, ParameterExpression doc,
            ParameterExpression updateBatch, ParameterExpression mapping, ParameterExpression currentVersion,
            ParameterExpression newVersion, ParameterExpression tenantId, bool useCharBufferPooling)
        {
            var argName = Expression.Constant(Arg);
            var dbType = Expression.Constant(NpgsqlDbType.Varchar);

            return Expression.Call(call, _paramMethod, argName, tenantId, dbType);
        }
    }
}