using System;
using System.IO;
using System.Reflection;
using Baseline;

namespace Marten.Schema
{
    public static class SchemaBuilder
    {
        public static string GetSqlScript(StoreOptions options, string script)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            var name = $"{typeof (SchemaBuilder).Namespace}.SQL.{script}.sql";

            return ReadFromStream(name, options.DatabaseSchemaName);
        }

        public static string GetJavascript(StoreOptions options, string jsfile)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            var name = $"{typeof (SchemaBuilder).Namespace}.SQL.{jsfile}.js";

            return ReadFromStream(name, options.DatabaseSchemaName);
        }

        public static StringWriter WriteSql(this StringWriter writer, StoreOptions options, string scriptName)
        {
            var format = GetSqlScript(options, scriptName);

            writer.WriteLine(format);
            writer.WriteLine();
            writer.WriteLine();

            return writer;
        }

        private static string ReadFromStream(string name, string databaseSchemaName)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null)
            {
                throw new InvalidOperationException("Could not find embedded resource: " + name);
            }
            var text = stream.ReadAllText();
            return text.Replace("{databaseSchema}", databaseSchemaName);
        }
    }
}