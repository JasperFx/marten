using System.Linq.Expressions;
using System.Reflection;
using Baseline.Reflection;
using Marten.Services;
using NpgsqlTypes;

namespace Marten.Schema.Arguments
{
    public class DocJsonBodyArgument : UpsertArgument
    {
        private static readonly MethodInfo _serializer = ReflectionHelper.GetProperty<UpdateBatch>(x => x.Serializer).GetMethod;
        private static readonly MethodInfo _tojson = typeof(ISerializer).GetMethod(nameof(ISerializer.ToJson));

        public DocJsonBodyArgument()
        {
            Arg = "doc";
            PostgresType = "JSONB";
            DbType = NpgsqlDbType.Jsonb;
            Column = "data";
        }

        public override Expression CompileUpdateExpression(EnumStorage enumStorage, ParameterExpression call, ParameterExpression doc, ParameterExpression updateBatch, ParameterExpression mapping, ParameterExpression currentVersion, ParameterExpression newVersion)
        {
            var argName = Expression.Constant(Arg);

            var serializer = Expression.Call(updateBatch, _serializer);
            var json = Expression.Call(serializer, _tojson, doc);

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