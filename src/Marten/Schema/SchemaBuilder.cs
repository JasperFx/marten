using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                new UpsertArgument {Arg = "doc", PostgresType = "JSON"}
            };

            var duplicates = mapping.DuplicatedFields.Select(x => x.UpsertArgument).ToArray();
            args.AddRange(duplicates);

            var argList = args.Select(x => x.ArgumentDeclaration()).Join(", ");
            var valueList = args.Select(x => x.Arg).Join(", ");

            var updates = "data = doc";
            if (duplicates.Any())
            {
                updates += ", " + duplicates.Select(x => $"{x.Column} = {x.Arg}").Join(", ");
            }


            writer.WriteLine($"CREATE OR REPLACE FUNCTION {mapping.UpsertName}({argList}) RETURNS VOID AS");
            writer.WriteLine("$$");
            writer.WriteLine("BEGIN");
            writer.WriteLine($"INSERT INTO {mapping.TableName} VALUES ({valueList})");
            writer.WriteLine($"  ON CONFLICT ON CONSTRAINT pk_{mapping.TableName}");
            writer.WriteLine($"  DO UPDATE SET {updates};");
            writer.WriteLine("END;");
            writer.WriteLine("$$ LANGUAGE plpgsql;");


            writer.WriteLine();
            writer.WriteLine();
        }


    }
}