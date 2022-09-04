using System.IO;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Testing;

public static class SchemaMigrationExtensions
{
    public static string UpdateSql(this SchemaMigration migration)
    {
        var writer = new StringWriter();
        migration.WriteAllUpdates(writer, new PostgresqlMigrator(), AutoCreate.CreateOrUpdate);
        return writer.ToString();
    }

    public static string RollbackSql(this SchemaMigration migration)
    {
        var writer = new StringWriter();
        migration.WriteAllRollbacks(writer, new PostgresqlMigrator());
        return writer.ToString();
    }
}
