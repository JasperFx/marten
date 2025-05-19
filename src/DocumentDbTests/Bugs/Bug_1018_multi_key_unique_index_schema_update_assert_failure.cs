using System;
using System.Threading.Tasks;
using JasperFx;
using Marten.Schema;
using Marten.Testing.Harness;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Doc_1018
{
    public Guid Id { get; set; }
    public string Field1 { get; set; }
    public string Field2 { get; set; }
}

public class Bug_1018_multi_key_unique_index_schema_update_assert_failure: BugIntegrationContext
{
    [Fact]
    public async Task check_database_matches_configuration_with_multi_key_unique_index()
    {
        StoreOptions(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            _.Schema.For<Doc_1018>()
                .Duplicate(x => x.Field1)
                .Duplicate(x => x.Field2)
                .UniqueIndex(UniqueIndexType.DuplicatedField, x => x.Field1, x => x.Field2);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

}
