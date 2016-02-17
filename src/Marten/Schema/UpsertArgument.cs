using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class UpsertArgument
    {
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