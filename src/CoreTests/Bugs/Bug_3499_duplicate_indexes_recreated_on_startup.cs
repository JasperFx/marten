using System;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Harness;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_3499_duplicate_indexes_recreated_on_startup : BugIntegrationContext
{
    [Fact]
    public async Task index_keeps_getting_recreated()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<TestDocumentModel>()
                .UniqueIndex(UniqueIndexType.DuplicatedField,
                    "testmodel_rm_index",
                    x => x.PropOne,
                    x => x.PropTwo,
                    x => x.Timestamp
                );
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store2 = SeparateStore(opts =>
        {
            opts.Schema.For<TestDocumentModel>()
                .UniqueIndex(UniqueIndexType.DuplicatedField,
                    "testmodel_rm_index",
                    x => x.PropOne,
                    x => x.PropTwo,
                    x => x.Timestamp
                );
        });

        await store2.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}

public sealed record TestDocumentModel(
    DateTimeOffset Timestamp,
    string PropOne,
    string PropTwo
)
{
    public string Id => $"{PropOne}_{PropTwo}_{Timestamp:yyyyMMddHHmmss}";
}
