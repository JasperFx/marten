using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Util;

namespace Marten.Schema
{
    public static class SchemaBuilder
    {
        public static void WriteSchemaObjects(DocumentMapping mapping, IDocumentSchema schema, StringWriter writer)
        {
            var table = mapping.ToTable(schema);
            table.Write(writer);
            writer.WriteLine();
            writer.WriteLine();

            var pgIdType = TypeMappings.GetPgType(mapping.IdMember.GetMemberType());

            var args = new List<UpsertArgument>
            {
                new UpsertArgument {Arg = "docId", PostgresType = pgIdType},
                new UpsertArgument {Arg = "doc", PostgresType = "JSONB"}
            };

            var duplicates = mapping.DuplicatedFields.Select(x => x.UpsertArgument).ToArray();
            args.AddRange(duplicates);

            var argList = args.Select(x => x.ArgumentDeclaration()).Join(", ");
            var valueList = args.Select(x => x.Arg).Join(", ");

            var updates = "data = doc";
            if (duplicates.Any())
            {
                updates += ", " + duplicates.Select(x => $"\"{x.Column}\" = {x.Arg}").Join(", ");
            }

            if (schema != null && schema.UpsertType == PostgresUpsertType.Legacy)
            {
                var inserts = "id, data";
                if (duplicates.Any())
                {
                    inserts += ", " + duplicates.Select(x => $"\"{x.Column}\"").Join(", ");
                }
                writer.WriteLine($"CREATE OR REPLACE FUNCTION {mapping.UpsertName}({argList}) RETURNS VOID AS");
                writer.WriteLine("$$");
                writer.WriteLine("BEGIN");
                writer.WriteLine($"LOCK TABLE {mapping.TableName} IN SHARE ROW EXCLUSIVE MODE;");
                writer.WriteLine($"  WITH upsert AS (UPDATE {mapping.TableName} SET {updates} WHERE id=docId RETURNING *) ");
                writer.WriteLine($"  INSERT INTO {mapping.TableName} ({inserts})");
                writer.WriteLine($"  SELECT {valueList} WHERE NOT EXISTS (SELECT * FROM upsert);");
                writer.WriteLine("END;");
                writer.WriteLine("$$ LANGUAGE plpgsql;");
            }
            else
            {
                writer.WriteLine($"CREATE OR REPLACE FUNCTION {mapping.UpsertName}({argList}) RETURNS VOID AS");
                writer.WriteLine("$$");
                writer.WriteLine("BEGIN");
                writer.WriteLine($"INSERT INTO {mapping.TableName} VALUES ({valueList})");
                writer.WriteLine($"  ON CONFLICT ON CONSTRAINT pk_{mapping.TableName}");
                writer.WriteLine($"  DO UPDATE SET {updates};");
                writer.WriteLine("END;");
                writer.WriteLine("$$ LANGUAGE plpgsql;");
            }

            mapping.Indexes.Each(x =>
            {
                writer.WriteLine();
                writer.WriteLine(x.ToDDL());
            });

            writer.WriteLine();
            writer.WriteLine();


        }

        public static void Write(StringWriter writer, string script)
        {
            var text = GetText(script);
            writer.WriteLine(text);
            writer.WriteLine();
            writer.WriteLine();
        }

        public static string GetText(string script)
        {
            var name = $"{typeof(SchemaBuilder).Namespace}.SQL.{script}.sql";

            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

            return stream.ReadAllText();
        }

        public static string GetJavascript(string jsfile)
        {
            var name = $"{typeof(SchemaBuilder).Namespace}.SQL.{jsfile}.js";

            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

            return stream.ReadAllText();
        }
    }
}