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
$$;";

        private readonly AdvancedOptions _advanced;

        public DatabaseSchemaGenerator(AdvancedOptions advanced)
        {
            if (advanced == null) throw new ArgumentNullException(nameof(advanced));

            _advanced = advanced;
        }

        public void Generate(string[] schemaNames)
        {
            if (schemaNames == null) throw new ArgumentNullException(nameof(schemaNames));

            var names = schemaNames
                .Distinct()
                .Where(name => name != StoreOptions.DefaultDatabaseSchemaName).ToList();

            if (!names.Any()) return;

            var sql = GenerateScript(names);
            using (var runner = _advanced.OpenConnection())
            {
                runner.Execute(sql);
            }
        }

        private static string GenerateScript(IEnumerable<string> schemaNames)
        {
            using (var writer = new StringWriter())
            {
                writer.Write(BeginScript);
                schemaNames.Each(name => WriteSql(name, writer));
                writer.Write(EndScript);

                return writer.ToString();
            }
        }

        private static void WriteSql(string databaseSchemaName, StringWriter writer)
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