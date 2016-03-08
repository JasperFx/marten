using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class UpsertFunction
    {
        public readonly IList<UpsertArgument> Arguments = new List<UpsertArgument>();
        public readonly IList<ColumnValue> Values = new List<ColumnValue>();



        public string FunctionName { get; }

        public UpsertFunction(DocumentMapping mapping)
        {
            FunctionName = mapping.UpsertName;
            TableName = mapping.TableName;

            var idType = mapping.IdMember.GetMemberType();
            PgIdType = TypeMappings.GetPgType(idType);
            Id_NpgsqlDbType = TypeMappings.ToDbType(idType);

            Arguments.Add(new UpsertArgument
            {
                Arg = "docId",
                PostgresType = PgIdType,
                Column = "id",
                Members = new[] {mapping.IdMember}
            });
            Arguments.Add(new UpsertArgument
            {
                Arg = "doc",
                PostgresType = "JSONB",
                DbType = NpgsqlDbType.Jsonb,
                Column = "data",
                BulkInsertPattern = "writer.Write(serializer.ToJson(x), NpgsqlDbType.Jsonb);",
                BatchUpdatePattern = "*"
            });
        }

        public NpgsqlDbType Id_NpgsqlDbType { get; }

        public string TableName { get; set; }

        public string PgIdType { get; }

        public void WriteFunctionSql(PostgresUpsertType upsertType, StringWriter writer)
        {
            var ordered = OrderedArguments();

            var argList = ordered.Select(x => x.ArgumentDeclaration()).Join(", ");

            var updates = ordered.Where(x => x.Column != "id")
                .Select(x => $"\"{x.Column}\" = {x.Arg}").Join(", ");

            var inserts = ordered.Select(x => $"\"{x.Column}\"").Join(", ");
            var valueList = ordered.Select(x => x.Arg).Join(", ");

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

        public UpsertArgument[] OrderedArguments()
        {
            return Arguments.OrderBy(x => x.Arg).ToArray();
        }

        public string ToUpdateBatchMethod(string typeName)
        {
            var parameters = OrderedArguments().Select(x => x.ToUpdateBatchParameter()).Join("");

            return $@"
BLOCK:public void RegisterUpdate(UpdateBatch batch, object entity)
var document = ({typeName})entity;
batch.Sproc(`{FunctionName}`){parameters.Replace("*", ".JsonEntity(`doc`, document)")};
END

BLOCK:public void RegisterUpdate(UpdateBatch batch, object entity, string json)
var document = ({typeName})entity;
batch.Sproc(`{FunctionName}`){parameters.Replace("*", ".JsonBody(`doc`, json)")};
END
";
        }

        public string ToBulkInsertMethod(string typeName)
        {
            var columns = OrderedArguments().Select(x => $"\\\"{x.Column}\\\"").Join(", ");

            var writerStatements = OrderedArguments()
                .Select(x => x.ToBulkInsertWriterStatement())
                .Join("\n");


            return $@"
BLOCK:public void Load(ISerializer serializer, NpgsqlConnection conn, IEnumerable<{typeName}> documents)
BLOCK:using (var writer = conn.BeginBinaryImport(`COPY {TableName}({columns}) FROM STDIN BINARY`))
BLOCK:foreach (var x in documents)
bool assigned = false;
Assign(x, out assigned);
writer.StartRow();
{writerStatements}
END
END
END

";
        }
    }
}