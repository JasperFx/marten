using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Marten.Services;
using Baseline;

namespace Marten.Schema
{
    public class DatabaseSchemaGenerator
    {
        private const string BeginScript = @"DO $$
BEGIN";
        private const string EndScript = @"END
$$;
";

        private readonly AdvancedOptions _advanced;

        public DatabaseSchemaGenerator(AdvancedOptions advanced)
        {
            if (advanced == null) throw new ArgumentNullException(nameof(advanced));

            _advanced = advanced;
        }

        public void Generate(StoreOptions options, string[] schemaNames)
        {
            if (schemaNames == null) throw new ArgumentNullException(nameof(schemaNames));

            var sql = GenerateScript(options, schemaNames);
            if (sql != null)
            {
                using (var runner = _advanced.OpenConnection())
                {
                    runner.Execute(sql);
                }
            }
        }

        public static string GenerateScript(StoreOptions options, IEnumerable<string> schemaNames)
        {
            if (schemaNames == null) throw new ArgumentNullException(nameof(schemaNames));

            var names = schemaNames
                 .Distinct()
                 .Where(name => name != StoreOptions.DefaultDatabaseSchemaName).ToList();

            if (!names.Any()) return null ;

            using (var writer = new StringWriter())
            {
                WriteSql(options, names, writer);

                return writer.ToString();
            }
        }

        public static void WriteSql(StoreOptions options, IEnumerable<string> schemaNames, TextWriter writer)
        {
            writer.Write(BeginScript);
            schemaNames.Each(name => WriteSql(options, name, writer));
            writer.WriteLine(EndScript);
        }

        private static void WriteSql(StoreOptions options, string databaseSchemaName, TextWriter writer)
        {
            writer.WriteLine($@"
    IF NOT EXISTS(
        SELECT schema_name
          FROM information_schema.schemata
          WHERE schema_name = '{databaseSchemaName}'
      )
    THEN
      EXECUTE 'CREATE SCHEMA {databaseSchemaName}';
    END IF;
");

        }
    }
}