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
        public static void WriteSchemaObjects(IDocumentMapping mapping, IDocumentSchema schema, StringWriter writer)
        {
            var table = mapping.ToTable(schema);
            table.Write(writer);
            writer.WriteLine();
            writer.WriteLine();

            mapping.ToUpsertFunction().WriteFunctionSql(schema?.UpsertType ?? PostgresUpsertType.Legacy, writer);

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