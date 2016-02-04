using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Schema
{
    /*
        public void RegisterUpdate(UpdateBatch batch, object entity)
        {
            var document = (Target)entity;
            batch.Sproc("mt_upsert_target").Param(document.Id, NpgsqlDbType.Uuid).JsonEntity(document).Param(document.Date, NpgsqlDbType.Date);
        }


        public void RegisterUpdate(UpdateBatch batch, object entity, string json)
        {
            var document = (Target)entity;
            batch.Sproc("mt_upsert_target").Param(document.Id, NpgsqlDbType.Uuid).JsonBody(json).Param(document.Date, NpgsqlDbType.Date);
        }



        public void Load(ISerializer serializer, NpgsqlConnection conn, IEnumerable<Target> documents)
        {
            using (var writer = conn.BeginBinaryImport("COPY mt_doc_target(id, data, date) FROM STDIN BINARY"))
            {
                foreach (var x in documents)
                {
                    writer.StartRow();
                    writer.Write(x.Id, NpgsqlDbType.Uuid);
                    writer.Write(serializer.ToJson(x), NpgsqlDbType.Jsonb);
                    writer.Write(x.Date, NpgsqlDbType.Date);
                }

            }

        }
    */


    public class ColumnValue
    {
        public ColumnValue(string column, string functionValue)
        {
            Column = column;
            FunctionValue = functionValue;
        }

        public string Column { get; }
        public string FunctionValue { get; }
    }

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
    }
}