using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Util;

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


    public class UpsertFunction
    {
        public readonly IList<UpsertArgument> Arguments = new List<UpsertArgument>();
        public readonly IList<ColumnValue> Values = new List<ColumnValue>();

        

        public string FunctionName { get; }

        public UpsertFunction(IDocumentMapping mapping)
        {
            FunctionName = mapping.UpsertName;
            TableName = mapping.TableName;

            PgIdType = TypeMappings.GetPgType(mapping.IdMember.GetMemberType());
            Arguments.Add(new UpsertArgument { Arg = "docId", PostgresType = PgIdType, Column = "id",  Members = new[] {mapping.IdMember}});
            Arguments.Add(new UpsertArgument { Arg = "doc", PostgresType = "JSONB", Column = "data"});
        }

        public string TableName { get; set; }

        public string PgIdType { get; }

        public void WriteFunctionSql(PostgresUpsertType upsertType, StringWriter writer)
        {
            var argList = Arguments.Select(x => x.ArgumentDeclaration()).Join(", ");

            var updates = Arguments.Where(x => x.Column != "id")
                .Select(x => $"\"{x.Column}\" = {x.Arg}").Join(", ");

            var inserts = Arguments.Select(x => x.Column).Join(", ");
            var valueList = Arguments.Select(x => x.Arg).Join(", ");

            if (upsertType == PostgresUpsertType.Legacy)
            {
                writer.WriteLine($"CREATE OR REPLACE FUNCTION {FunctionName}({argList}) RETURNS VOID AS");
                writer.WriteLine("$$");
                writer.WriteLine("BEGIN");
                writer.WriteLine($"LOCK TABLE {TableName} IN SHARE ROW EXCLUSIVE MODE;");
                writer.WriteLine($"  WITH upsert AS (UPDATE {TableName} SET {updates} WHERE id=docId RETURNING *) ");
                writer.WriteLine($"  INSERT INTO {TableName} ({inserts})");
                writer.WriteLine($"  SELECT {valueList} WHERE NOT EXISTS (SELECT * FROM upsert);");
                writer.WriteLine("END;");
                writer.WriteLine("$$ LANGUAGE plpgsql;");
            }
            else
            {
                writer.WriteLine($"CREATE OR REPLACE FUNCTION {FunctionName}({argList}) RETURNS VOID AS");
                writer.WriteLine("$$");
                writer.WriteLine("BEGIN");
                writer.WriteLine($"INSERT INTO {TableName} VALUES ({valueList})");
                writer.WriteLine($"  ON CONFLICT ON CONSTRAINT pk_{TableName}");
                writer.WriteLine($"  DO UPDATE SET {updates};");
                writer.WriteLine("END;");
                writer.WriteLine("$$ LANGUAGE plpgsql;");
            }

        }

        public string ToUpdateBatchMethod(string typeName)
        {
            throw new NotImplementedException();
        }

        public string ToBulkInsertMethod(string typeName)
        {
            throw new NotImplementedException();
        }
    }


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
        public string Arg { get; set; }
        public string PostgresType { get; set; }

        public string Column { get; set; }

        public string ArgumentDeclaration()
        {
            return $"{Arg} {PostgresType}";
        }

        public MemberInfo[] Members { get; set; }

    }
}