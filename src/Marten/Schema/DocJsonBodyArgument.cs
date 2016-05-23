using System.Linq.Expressions;
using System.Reflection;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class DocJsonBodyArgument : UpsertArgument
    {
        private static readonly MethodInfo _tojson = typeof(ISerializer).GetMethod(nameof(ISerializer.ToJson));

        public DocJsonBodyArgument()
        {
            Arg = "doc";
            PostgresType = "JSONB";
            DbType = NpgsqlDbType.Jsonb;
            Column = "data";
        }

        public override Expression CompileUpdateExpression(EnumStorage enumStorage, ParameterExpression call, ParameterExpression doc, ParameterExpression json, ParameterExpression mapping)
        {
            var argName = Expression.Constant(Arg);
            return Expression.Call(call, _paramMethod, argName, json, Expression.Constant(NpgsqlDbType.Jsonb));
        }

        public override Expression CompileBulkImporter(EnumStorage enumStorage, Expression writer, ParameterExpression document, ParameterExpression alias, ParameterExpression serializer)
        {
            var json = Expression.Call(serializer, _tojson, document);
            var method = writeMethod.MakeGenericMethod(typeof(string));
            var dbType = Expression.Constant(DbType);

            return Expression.Call(writer, method, json, dbType);
        }
    }
}