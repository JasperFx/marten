using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_3145_migration_to_tenanted : BugIntegrationContext
{
    [Fact]
    public async Task can_retrofit_to_multitenanted_later()
    {
        var mapping = new DocumentMapping(typeof(TestDoc), new StoreOptions{DatabaseSchemaName = "bugs"});

        var table = new DocumentTable(mapping);

        using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await table.CreateAsync(conn);

        mapping.TenancyStyle = TenancyStyle.Conjoined;
        var table2 = new DocumentTable(mapping);
        var migration = await SchemaMigration.DetermineAsync(conn, table2);
        await new PostgresqlMigrator().ApplyAllAsync(conn, migration, AutoCreate.CreateOrUpdate);

        var delta = await table2.FindDeltaAsync(conn);

        if (delta.Difference != SchemaPatchDifference.None)
        {
            var writer = new StringWriter();
            delta.WriteUpdate(new PostgresqlMigrator(), writer);

            throw new Exception("Found delta:\n" + writer.ToString());
        }


    }
}

public record TestDoc(Guid Id, string Column);
