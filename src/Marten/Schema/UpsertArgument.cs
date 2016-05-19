using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class UpsertArgument
    {
        protected static readonly MethodInfo writeMethod =
            typeof(NpgsqlBinaryImporter).GetMethods().FirstOrDefault(x => x.GetParameters().Length == 2);

        protected static readonly MethodInfo _paramMethod = typeof(BatchCommand.SprocCall)
            .GetMethod("Param", new[] {typeof(string), typeof(object), typeof(NpgsqlDbType)});

        private MemberInfo[] _members;
        public string Arg { get; set; }
        public string PostgresType { get; set; }

        public string Column { get; set; }

        public MemberInfo[] Members
        {
            get { return _members; }
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

        public virtual Expression CompileBulkImporter(EnumStorage enumStorage, Expression writer, ParameterExpression document, ParameterExpression alias, ParameterExpression serializer)
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


        public virtual Expression CompileUpdateExpression(EnumStorage enumStorage, ParameterExpression call,
            ParameterExpression doc, ParameterExpression json, ParameterExpression mapping,
            ParameterExpression typeAlias)
        {
            var argName = Expression.Constant(Arg);

            var memberType = Members.Last().GetMemberType();
            var body = LambdaBuilder.ToExpression(enumStorage, Members, doc);
            if (!memberType.IsClass)
            {
                body = Expression.Convert(body, typeof(object));
            }


            return Expression.Call(call, _paramMethod, argName, body, Expression.Constant(DbType));
        }
    }
}