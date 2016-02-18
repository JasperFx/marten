using System.IO;
using System.Reflection;
using Baseline;

namespace Marten.Schema
{
    public static class SchemaBuilder
    {
        public static void Write(StringWriter writer, string script)
        {
            var text = GetText(script);
            writer.WriteLine(text);
            writer.WriteLine();
            writer.WriteLine();
        }

        public static string GetText(string script)
        {
            var name = $"{typeof (SchemaBuilder).Namespace}.SQL.{script}.sql";

            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

            return stream.ReadAllText();
        }

        public static string GetJavascript(string jsfile)
        {
            var name = $"{typeof (SchemaBuilder).Namespace}.SQL.{jsfile}.js";

            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

            return stream.ReadAllText();
        }
    }
}