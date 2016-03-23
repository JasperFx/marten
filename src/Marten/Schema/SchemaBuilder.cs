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
            var name = $"{typeof (SchemaBuilder).Namespace}.SQL.{script}.sql";

            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null)
            {
                throw new InvalidOperationException("Could not find embedded resource: " + name);
            }
            var text = stream.ReadAllText();    
            return text.Replace("{databaseSchema}", options.DatabaseSchemaName);
        }

        public static string GetJavascript(string jsfile)
        {
            var name = $"{typeof (SchemaBuilder).Namespace}.SQL.{jsfile}.js";

            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

            return stream.ReadAllText();
        }

        public static StringWriter WriteSql(this StringWriter writer, StoreOptions options, string scriptName)
        {
            var format = GetSqlScript(options, scriptName);

            writer.WriteLine(format);
            writer.WriteLine();
            writer.WriteLine();

            return writer;
        }
    }
}