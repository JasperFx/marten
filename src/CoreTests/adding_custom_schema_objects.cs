using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Functions;
using Weasel.Postgresql.Tables;
using Xunit;

namespace CoreTests;

public class adding_custom_schema_objects : OneOffConfigurationsContext
{
    [Fact]
    public void extension_feature_is_not_active_without_any_extended_objects()
    {
        theStore.Options.Storage.AllActiveFeatures(theStore.Storage.Database)
            .OfType<StorageFeatures>().Any().ShouldBeFalse();
    }

    [Fact]
    public void extension_feature_is_active_with_custom_extended_objects()
    {
        var table = new Table("names");
        table.AddColumn<string>("name").AsPrimaryKey();
        theStore.Options.Storage.ExtendedSchemaObjects.Add(table);

        var feature = theStore.Options.Storage.AllActiveFeatures(theStore.Storage.Database)
            .OfType<StorageFeatures>().Single().As<IFeatureSchema>();

        feature.Objects.Single().ShouldBeTheSameAs(table);
    }

    [Fact]
    public async Task build_a_table()
    {
        // The schema is dropped when this method is called, so existing
        // tables would be dropped first
        #region sample_CustomSchemaTable
        StoreOptions(opts =>
        {
            opts.RegisterDocumentType<Target>();

            var table = new Table("adding_custom_schema_objects.names");
            table.AddColumn<string>("name").AsPrimaryKey();

            opts.Storage.ExtendedSchemaObjects.Add(table);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        #endregion


        using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var tableNames = await conn.ExistingTables(schemas: new[] { "adding_custom_schema_objects" });
        tableNames.Any(x => x.Name == "names").ShouldBeTrue();
    }

    [Fact]
    public async Task enable_an_extension()
    {
        #region sample_CustomSchemaExtension
        StoreOptions(opts =>
        {
            opts.RegisterDocumentType<Target>();

            // Unaccent is an extension ships with postgresql
            // and removes accents (diacritic signs) from strings
            var extension = new Extension("unaccent");

            opts.Storage.ExtendedSchemaObjects.Add(extension);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        #endregion

        var session = theStore.OpenSession();

        var result = await session.QueryAsync<bool>("select unaccent('Æ') = 'AE';");

        result.First().ShouldBe(true);
    }

    [Fact]
    public async Task create_a_function()
    {
        #region sample_CustomSchemaFunction
        StoreOptions(opts =>
        {
            opts.RegisterDocumentType<Target>();

            // Create a user defined function to act as a ternary operator similar to SQL Server
            var function = new Function(new DbObjectName("public", "iif"), @"
create or replace function iif(
    condition boolean,       -- if condition
    true_result anyelement,  -- then
    false_result anyelement  -- else
) returns anyelement as $f$
  select case when condition then true_result else false_result end
$f$  language sql immutable;
");

            opts.Storage.ExtendedSchemaObjects.Add(function);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        #endregion

        var session = theStore.OpenSession();

        var match = await session.QueryAsync<string>("select iif(1 = 1, 'value matches'::text, 'no match'::text);");
        var noMatch = await session.QueryAsync<string>("select iif(1 = 2, 'value matches'::text, 'no match'::text);");

        match.First().ShouldBe("value matches");
        noMatch.First().ShouldBe("no match");
    }

    [Fact]
    public async Task create_a_sequence()
    {
        #region sample_CustomSchemaSequence
        StoreOptions(opts =>
        {
            opts.RegisterDocumentType<Target>();

            // Create a sequence to generate unique ids for documents
            var sequence = new Sequence("banana_seq");

            opts.Storage.ExtendedSchemaObjects.Add(sequence);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        #endregion

        var session = theStore.OpenSession();

        var value = await session.QueryAsync<int>("select nextval('banana_seq')");
        var valueAgain = await session.QueryAsync<int>("select nextval('banana_seq')");

        valueAgain.First().ShouldBe(value.First() + 1);
    }
}