using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Weasel.Postgresql;
using Marten.Services;
using Marten.Storage;
using Weasel.Core;

namespace Marten.Schema
{
    internal class DatabaseSchemaGenerator
    {
        private const string BeginScript = @"DO $$
BEGIN";

        private const string EndScript = @"END
$$;
";

        private readonly ITenant _tenant;

        public DatabaseSchemaGenerator(ITenant tenant)
        {
            _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
        }

        public void Generate(StoreOptions options, string[] schemaNames)
        {
            if (schemaNames == null)
                throw new ArgumentNullException(nameof(schemaNames));

            var sql = GenerateScript(options, schemaNames);
            if (sql != null)
            {
                using (var runner = _tenant.OpenConnection())
                {
                    runner.Execute(sql);
                }
            }
        }

        public static string GenerateScript(StoreOptions options, IEnumerable<string> schemaNames)
        {
            if (schemaNames == null)
                throw new ArgumentNullException(nameof(schemaNames));

            var names = schemaNames
                 .Distinct()
                 .Where(name => name != SchemaConstants.DefaultSchema).ToList();

            if (!names.Any())
                return null;

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
