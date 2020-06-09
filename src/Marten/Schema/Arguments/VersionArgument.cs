using System;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using NpgsqlTypes;

namespace Marten.Schema.Arguments
{
    public class VersionArgument: UpsertArgument
    {
        public const string ArgName = "docVersion";

        private readonly static MethodInfo _newGuid =
            typeof(Guid).GetMethod(nameof(Guid.NewGuid),
                BindingFlags.Static | BindingFlags.Public);

        public VersionArgument()
        {
            Arg = ArgName;
            Column = DocumentMapping.VersionColumn;
            DbType = NpgsqlDbType.Uuid;
            PostgresType = "uuid";
        }

        public override Expression CompileBulkImporter(DocumentMapping mapping, EnumStorage enumStorage, Expression writer, ParameterExpression document, ParameterExpression alias, ParameterExpression serializer, ParameterExpression textWriter, ParameterExpression tenantId)
        {
            Expression value = Expression.Call(_newGuid);

            var dbType = Expression.Constant(DbType);

            var method = writeMethod.MakeGenericMethod(typeof(Guid));

            var writeExpression = Expression.Call(writer, method, value, dbType);
            if (mapping.VersionMember == null)
            {
                return writeExpression;
            }
            else if (mapping.VersionMember is FieldInfo)
            {
                var fieldAccess = Expression.Field(document, (FieldInfo)mapping.VersionMember);
                var fieldSetter = Expression.Assign(fieldAccess, value);

                return Expression.Block(fieldSetter, writeExpression);
            }
            else
            {
                var property = mapping.VersionMember.As<PropertyInfo>();
                var setMethod = property.SetMethod;
                var callSetMethod = Expression.Call(document, setMethod, value);

                return Expression.Block(callSetMethod, writeExpression);
            }
        }

        public override Expression CompileUpdateExpression(EnumStorage enumStorage, ParameterExpression call, ParameterExpression doc, ParameterExpression updateBatch, ParameterExpression mapping, ParameterExpression currentVersion, ParameterExpression newVersion, ParameterExpression tenantId, bool useCharBufferPooling)
        {
            var dbType = Expression.Constant(DbType);
            return Expression.Call(call, _paramMethod, Expression.Constant(Arg), Expression.Convert(newVersion, typeof(object)), dbType);
        }

        public override void GenerateCode(GeneratedMethod method, GeneratedType type, int i, Argument parameters)
        {
            method.Frames.Code("setVersionParameter({0}[{1}]);", parameters, i);
        }
    }
}
