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

        static readonly MethodInfo _writerBuffer = ReflectionHelper.GetProperty<CharArrayTextWriter>(x => x.Buffer).GetMethod;
        static readonly MethodInfo _writerSize = ReflectionHelper.GetProperty<CharArrayTextWriter>(x => x.Size).GetMethod;

        public DocJsonBodyArgument()
        {
            Arg = "doc";
            PostgresType = "JSONB";
            DbType = NpgsqlDbType.Jsonb;
            Column = "data";
        }

        public override Expression CompileUpdateExpression(EnumStorage enumStorage, ParameterExpression call, ParameterExpression doc, ParameterExpression updateBatch, ParameterExpression mapping, ParameterExpression currentVersion, ParameterExpression newVersion, bool useCharBufferPooling)
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

                var buffer = Expression.Call(writer, _writerBuffer);
                var size = Expression.Call(writer, _writerSize);

                return Expression.Block(new[] { writer },
                    Expression.Assign(writer, Expression.Call(updateBatch, _getWriter)),
                    Expression.Call(serializer, _tojsonWithWriter, doc, writer),
                    Expression.Call(call, _paramWithSizeMethod, argName, buffer, jsonb, size)
                );
            }
        }

        public override Expression CompileBulkImporter(EnumStorage enumStorage, Expression writer, ParameterExpression document, ParameterExpression alias, ParameterExpression serializer, ParameterExpression textWriter, bool useCharBufferPooling)
        {
            if (useCharBufferPooling)
            {
                var method = writeMethod.MakeGenericMethod(typeof(ArraySegment<char>));
                var dbType = Expression.Constant(DbType);

                return Expression.Block(
                    Expression.Call(serializer, _tojsonWithWriter, document, textWriter),
                    Expression.Call(writer, method, Expression.Call(textWriter, _toSegment), dbType)
                    );
            }
            else
            {
                var json = Expression.Call(serializer, _tojson, document);
                var method = writeMethod.MakeGenericMethod(typeof(string));
                var dbType = Expression.Constant(DbType);

                return Expression.Call(writer, method, json, dbType);
            }
        }
    }
}