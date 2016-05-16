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

        public Expression CompileBulkImporter<T>(Expression writer, ParameterExpression document)
        {
            var memberType = Members.Last().GetMemberType();
            var method = writeMethod.MakeGenericMethod(memberType);

            var value = LambdaBuilder.ToExpression(Members, document);

            var dbType = Expression.Constant(DbType);
            var call = Expression.Call(writer, method, value, dbType);

            return call;
        }

        public NpgsqlDbType DbType { get; set; }

        [Obsolete("Goes away after the Roslyn generation is gone")]
        public string BatchUpdatePattern = ".Param(\"{2}\", document.{0}, NpgsqlDbType.{1})";

        [Obsolete("Goes away after the Roslyn generation is gone")]
        public string ToUpdateBatchParameter()
        {
            if (Members == null) return BatchUpdatePattern;

            var accessor = Members.Select(x => x.Name).Join("?.");

            return BatchUpdatePattern.ToFormat(accessor, DbType, Arg);
        }

        private readonly static MethodInfo _paramMethod = typeof(BatchCommand.SprocCall)
            .GetMethod("Param", new Type[] {typeof(string), typeof(object), typeof(NpgsqlDbType)});



        public Expression CompileUpdateExpression(ParameterExpression call, ParameterExpression doc,
            ParameterExpression json, ParameterExpression mapping, ParameterExpression typeAlias)
        {
            var argName = Expression.Constant(Arg);

            if (Members != null)
            {
                var memberType = Members.Last().GetMemberType();
                Expression body = LambdaBuilder.ToExpression(Members, doc);
                if (!memberType.IsClass)
                {
                    body = Expression.Convert(body, typeof(object));
                }

                NpgsqlDbType dbType = TypeMappings.ToDbType(memberType);

                return Expression.Call(call, _paramMethod, argName, body, Expression.Constant(dbType));
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