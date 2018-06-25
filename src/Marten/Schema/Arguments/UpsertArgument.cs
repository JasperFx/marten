using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema.Arguments
{
    public class UpsertArgument
    {
        protected static readonly MethodInfo writeMethod =
            typeof(NpgsqlBinaryImporter).GetMethods().FirstOrDefault(x => x.GetParameters().Length == 2);

        protected static readonly MethodInfo _paramMethod = typeof(SprocCall)
            .GetMethod("Param", new[] {typeof(string), typeof(object), typeof(NpgsqlDbType)});

        protected static readonly MethodInfo _paramWithSizeMethod = typeof(SprocCall)
            .GetMethod("Param", new[] { typeof(string), typeof(object), typeof(NpgsqlDbType), typeof(int) });

        private MemberInfo[] _members;
        private string _postgresType;
        public string Arg { get; set; }

        public string PostgresType
        {
            get => _postgresType;
            set
            {
                if (value == null) throw new ArgumentNullException();

                _postgresType = value.Contains("(") 
                    ? value.Split('(')[0].Trim() 
                    : value;
            }
        }

        public string Column { get; set; }

        public MemberInfo[] Members
        {
            get => _members;
            set
            {
                _members = value;
                if (value != null)
                {
                    DbType = TypeMappings.ToDbType(value.Last().GetMemberType());
                }
            }
        }

        public NpgsqlDbType DbType { get; set; }

        public string ArgumentDeclaration()
        {
            return $"{Arg} {PostgresType}";
        }

        public virtual Expression CompileBulkImporter(DocumentMapping mapping, EnumStorage enumStorage, Expression writer, ParameterExpression document, ParameterExpression alias, ParameterExpression serializer, ParameterExpression textWriter, ParameterExpression tenantId)
        {
            var memberType = Members.Last().GetMemberType();


            var value = LambdaBuilder.ToExpression(enumStorage, Members, document);

            if (memberType.IsEnum)
            {
                memberType = typeof(string);
                value = LambdaBuilder.ToExpression(EnumStorage.AsString, Members, document);
            }

            var method = writeMethod.MakeGenericMethod(memberType);

            var dbType = Expression.Constant(DbType);

            return Expression.Call(writer, method, value, dbType);
        }


        public virtual Expression CompileUpdateExpression(EnumStorage enumStorage, ParameterExpression call, ParameterExpression doc, ParameterExpression updateBatch, ParameterExpression mapping, ParameterExpression currentVersion, ParameterExpression newVersion, ParameterExpression tenantId, bool useCharBufferPooling)
        {
            var argName = Expression.Constant(Arg);

            var memberType = Members.Last().GetMemberType();
            var body = LambdaBuilder.ToExpression(enumStorage, Members, doc);
            if (!memberType.GetTypeInfo().IsClass)
            {
                body = Expression.Convert(body, typeof(object));
            }


            return Expression.Call(call, _paramMethod, argName, body, Expression.Constant(DbType));
        }
    }
}