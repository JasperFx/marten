using System;
using System.IO;

namespace Marten.Schema
{
    public static class EnsureDatabaseSchema
    {
        public static void WriteSql(string databaseSchemaName, StringWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (String.IsNullOrWhiteSpace(databaseSchemaName)) throw new ArgumentException("Argument is null or whitespace", nameof(databaseSchemaName));

            writer.WriteLine($@"
DO $$
BEGIN
    IF NOT EXISTS(
        SELECT schema_name
          FROM information_schema.schemata
          WHERE schema_name = '{databaseSchemaName}'
      )
    THEN
      EXECUTE 'CREATE SCHEMA {databaseSchemaName}';
    END IF;
END
$$;
            ");
        }
    }
}