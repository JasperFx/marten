using System;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_3430_warn_when_indexes_are_too_big : BugIntegrationContext
{
    [Fact]
    public async Task get_useful_warning_from_apply_all_changes()
    {
        var indexName = "testmodel_rm_index_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long";

        StoreOptions(opts =>
        {
            opts.Schema.For<TestDocumentModel>()
                .UniqueIndex(UniqueIndexType.DuplicatedField,
                    indexName,
                    x => x.PropOne,
                    x => x.PropTwo,
                    x => x.Timestamp
                );
        });

        await Should.ThrowAsync<PostgresqlIdentifierTooLongException>(async () =>
        {
            await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        });
    }

    [Fact]
    public async Task get_useful_warning_from_ensure_storage_exists()
    {
        var indexName = "testmodel_rm_index_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long_long";

        StoreOptions(opts =>
        {
            opts.Schema.For<TestDocumentModel>()
                .UniqueIndex(UniqueIndexType.DuplicatedField,
                    indexName,
                    x => x.PropOne,
                    x => x.PropTwo,
                    x => x.Timestamp
                );
        });

        await Should.ThrowAsync<PostgresqlIdentifierTooLongException>(async () =>
        {
            await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(TestDocumentModel));
        });
    }

}

public class AppointmentScheduling
{
    public Guid Id { get; set; }

    public string Supercalifragilisticexpialidocious1 { get; set; }
    public string Supercalifragilisticexpialidocious2 { get; set; }
    public string Supercalifragilisticexpialidocious3 { get; set; }

    public string ReallyReallyRealReallyReallyReallyReallyReallyReallyReallyReallyReallyReallyReallyReallyReallyReallyReallyReallyReallyReallyReallyReallyReallyLong
    {
        get;
        set;
    }
}
