using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Baseline.Reflection;
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

        public string BulkInsertPattern = "writer.Write(x.{0}, NpgsqlDbType.{1});";

        public string ToBulkInsertWriterStatement()
        {
            if (Members == null) return BulkInsertPattern;

            var accessor = Members.Select(x => x.Name).Join("?.");
            return BulkInsertPattern.ToFormat(accessor, DbType);
        }

        public string BatchUpdatePattern = ".Param(\"{2}\", document.{0}, NpgsqlDbType.{1})";

        public string ToUpdateBatchParameter()
        {
            if (Members == null) return BatchUpdatePattern;

            var accessor = Members.Select(x => x.Name).Join("?.");

            return BatchUpdatePattern.ToFormat(accessor, DbType, Arg);
        }
    }
}