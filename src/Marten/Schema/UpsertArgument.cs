using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Baseline.Reflection;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class UpsertArgument
    {
        private readonly static MethodInfo writeMethod =
            typeof(NpgsqlBinaryImporter).GetMethods().FirstOrDefault(x => x.GetParameters().Length == 2);

        private MemberInfo[] _members;
        public string Arg { get; set; }
        public string PostgresType { get; set; }

        public string Column { get; set; }

        public string ArgumentDeclaration()
        {
            return $"{Arg} {PostgresType}";
        }

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

        public Expression CompileBulkImporter(EnumStorage enumStorage, Expression writer, ParameterExpression document)
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
            var call = Expression.Call(writer, method, value, dbType);

            return call;
        }

        public NpgsqlDbType DbType { get; set; }

        private readonly static MethodInfo _paramMethod = typeof(BatchCommand.SprocCall)
            .GetMethod("Param", new Type[] {typeof(string), typeof(object), typeof(NpgsqlDbType)});



        public Expression CompileUpdateExpression(EnumStorage enumStorage, ParameterExpression call, ParameterExpression doc, ParameterExpression json, ParameterExpression mapping, ParameterExpression typeAlias)
        {
            var argName = Expression.Constant(Arg);

            if (Members != null)
            {
                var memberType = Members.Last().GetMemberType();
                Expression body = LambdaBuilder.ToExpression(enumStorage, Members, doc);
                if (!memberType.IsClass)
                {
                    body = Expression.Convert(body, typeof(object));
                }


                return Expression.Call(call, _paramMethod, argName, body, Expression.Constant(DbType));
            }

            if (Arg == "docType")
            {
                return Expression.Call(call, _paramMethod, argName, typeAlias, Expression.Constant(NpgsqlDbType.Varchar));
            }

            if (Arg == "doc")
            {
                return Expression.Call(call, _paramMethod, argName, json, Expression.Constant(NpgsqlDbType.Jsonb));
            }

            throw new InvalidOperationException($"Don't know how to create an upsert argument expression for Arg == {Arg}");
        }
    }
}