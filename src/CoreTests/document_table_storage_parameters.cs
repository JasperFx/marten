using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests;

public class DocumentTableStorageParametersSamples
{
    public static void configure_table_storage_parameters(StoreOptions opts)
    {
        #region sample_configuring_document_table_storage_parameters

        opts.Schema.For<Target>()
            // WITH (fillfactor = 90) -- leaves free space in each page for updated row versions
            .FillFactor(90)
            // Any other table level storage parameter by name
            .StorageParameter("autovacuum_vacuum_scale_factor", 0.01);

        #endregion
    }
}

/// <summary>
///     Table level storage parameters on a document table: <c>WITH (...)</c> in the DDL, and a migration that
///     only manages the parameters the mapping declares.
/// </summary>
public class document_table_storage_parameters: OneOffConfigurationsContext
{
    private string tableDdl(DocumentStore store)
    {
        var table = store.Options.Storage.MappingFor(typeof(Target)).Schema.Table;
        var writer = new StringWriter();
        table.WriteCreateStatement(new PostgresqlMigrator(), writer);
        return writer.ToString();
    }

    private async Task<string[]> reloptions()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var command = conn.CreateCommand();
        command.CommandText = "select reloptions from pg_class where oid = to_regclass(:name)";
        command.Parameters.AddWithValue("name", $"{SchemaName}.mt_doc_target");

        var value = await command.ExecuteScalarAsync();
        return value as string[] ?? [];
    }

    [Fact]
    public void ddl_contains_the_with_clause()
    {
        StoreOptions(opts => opts.Schema.For<Target>()
            .FillFactor(90)
            .StorageParameter("autovacuum_vacuum_scale_factor", 0.01));

        tableDdl(theStore).ShouldContain("WITH (fillfactor = 90, autovacuum_vacuum_scale_factor = 0.01)");
    }

    [Fact]
    public void ddl_without_parameters_has_no_with_clause()
    {
        StoreOptions(opts => opts.Schema.For<Target>());

        tableDdl(theStore).ShouldNotContain("WITH (");
    }

    [Fact]
    public async Task applying_the_parameters_leaves_no_delta()
    {
        StoreOptions(opts => opts.Schema.For<Target>()
            .FillFactor(90)
            .StorageParameter("autovacuum_vacuum_scale_factor", 0.01));

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await reloptions()).ShouldBe(["fillfactor=90", "autovacuum_vacuum_scale_factor=0.01"], ignoreOrder: true);

        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();

        var migration = await SeparateStore(opts => opts.Schema.For<Target>()
            .FillFactor(90)
            .StorageParameter("autovacuum_vacuum_scale_factor", 0.01)).Storage.CreateMigrationAsync();
        migration.Difference.ShouldBe(SchemaPatchDifference.None);
    }

    [Fact]
    public async Task changing_a_parameter_alters_the_table_and_converges()
    {
        StoreOptions(opts => opts.Schema.For<Target>().FillFactor(90));
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store2 = SeparateStore(opts => opts.Schema.For<Target>().FillFactor(70));

        var migration = await store2.Storage.Database.CreateMigrationAsync();
        migration.Difference.ShouldBe(SchemaPatchDifference.Update);
        var sql = new StringWriter();
        migration.WriteAllUpdates(sql, new PostgresqlMigrator(), AutoCreate.CreateOrUpdate);
        sql.ToString().ShouldContain("SET (fillfactor = 70)", Case.Insensitive);

        await store2.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await reloptions()).ShouldBe(["fillfactor=70"]);
        await store2.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task adding_a_parameter_to_an_existing_table_alters_it()
    {
        StoreOptions(opts => opts.Schema.For<Target>());
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        (await reloptions()).ShouldBeEmpty();

        var store2 = SeparateStore(opts => opts.Schema.For<Target>().FillFactor(85));
        (await store2.Storage.CreateMigrationAsync()).Difference.ShouldBe(SchemaPatchDifference.Update);

        await store2.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        (await reloptions()).ShouldBe(["fillfactor=85"]);
    }

    [Fact]
    public async Task undeclared_parameters_in_the_database_are_ignored()
    {
        StoreOptions(opts => opts.Schema.For<Target>().FillFactor(90)
            .StorageParameter("autovacuum_enabled", false));
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // The mapping no longer declares autovacuum_enabled; the database keeps it and there is no delta
        var store2 = SeparateStore(opts => opts.Schema.For<Target>().FillFactor(90));
        (await store2.Storage.CreateMigrationAsync()).Difference.ShouldBe(SchemaPatchDifference.None);
        (await reloptions()).Length.ShouldBe(2);
    }
}
