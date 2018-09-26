using System;
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
        private static readonly MethodInfo _getWriter = typeof(UpdateBatch).GetMethod(nameof(UpdateBatch.GetWriter));
        private static readonly MethodInfo _toSegment = typeof(CharArrayTextWriter).GetMethod(nameof(CharArrayTextWriter.ToCharSegment));
        private static readonly MethodInfo _tojson = typeof(ISerializer).GetMethod(nameof(ISerializer.ToJson), new[] { typeof(object) });
        private static readonly MethodInfo _tojsonWithWriter = typeof(ISerializer).GetMethod(nameof(ISerializer.ToJson), new[] { typeof(object), typeof(CharArrayTextWriter) });

        static readonly MethodInfo _writerToSegment = ReflectionHelper.GetMethod<CharArrayTextWriter>(x => x.ToCharSegment());

        public DocJsonBodyArgument()
        {
            Arg = "doc";
            PostgresType = "JSONB";
            DbType = NpgsqlDbType.Jsonb;
            Column = "data";
        }

        public override Expression CompileUpdateExpression(EnumStorage enumStorage, ParameterExpression call, ParameterExpression doc, ParameterExpression updateBatch, ParameterExpression mapping, ParameterExpression currentVersion, ParameterExpression newVersion, ParameterExpression tenantId, bool useCharBufferPooling)
        {
            var argName = Expression.Constant(Arg);
            var serializer = Expression.Call(updateBatch, _serializer);
            var jsonb = Expression.Constant(NpgsqlDbType.Jsonb);

            if (useCharBufferPooling == false)
            {
                var json = Expression.Call(serializer, _tojson, doc);
                return Expression.Call(call, _paramMethod, argName, json, jsonb);
            }
            else
            {
                var writer = Expression.Variable(typeof(CharArrayTextWriter), "writer");
                var segment = Expression.Variable(typeof(ArraySegment<char>), "segment");

                return Expression.Block(new[] { writer, segment },
                    Expression.Assign(writer, Expression.Call(updateBatch, _getWriter)),
                    Expression.Call(serializer, _tojsonWithWriter, doc, writer),
                    Expression.Assign(segment, Expression.Call(writer,_writerToSegment)),
                    Expression.Call(call, _paramWithJsonBody, argName, segment)
                );
            }
        }

        public override Expression CompileBulkImporter(DocumentMapping mapping, EnumStorage enumStorage, Expression writer, ParameterExpression document, ParameterExpression alias, ParameterExpression serializer, ParameterExpression textWriter, ParameterExpression tenantId)
        {
            var method = writeMethod.MakeGenericMethod(typeof(ArraySegment<char>));
            var dbType = Expression.Constant(DbType);

            return Expression.Block(
                Expression.Call(serializer, _tojsonWithWriter, document, textWriter),
                Expression.Call(writer, method, Expression.Call(textWriter, _toSegment), dbType)
                );
        }
    }
}