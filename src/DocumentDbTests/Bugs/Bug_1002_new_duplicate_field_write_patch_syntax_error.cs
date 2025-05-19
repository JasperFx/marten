using System.Threading.Tasks;
using JasperFx;
using Marten.Testing;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_1002_new_duplicate_field_write_patch_syntax_error: BugIntegrationContext
{
    [Fact]
    public async Task update_patch_should_not_contain_double_semicolon()
    {
        StoreOptions(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            _.Advanced.Migrator.TableCreation = CreationStyle.CreateIfNotExists;
            _.Schema.For<Bug_1002>();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store = SeparateStore(_ =>
        {
            _.Connection(ConnectionSource.ConnectionString);
            _.Schema.For<Bug_1002>()
                .Duplicate(x => x.Name); // add a new duplicate column
        });

        (await store.Storage.Database.CreateMigrationAsync()).UpdateSql().ShouldNotContain(";;");
    }

}

public class Bug_1002
{
    public string Id { get; set; }
    public string Name { get; set; }
}
