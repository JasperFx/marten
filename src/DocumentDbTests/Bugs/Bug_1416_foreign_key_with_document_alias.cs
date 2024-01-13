using System;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.Bugs;

public class Bug_1416_foreign_key_with_document_alias : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_1416_foreign_key_with_document_alias(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task use_the_correct_naming()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<OriginalNameEntity>()
                .Identity(x => x.Id)
                .ForeignKey<RelatedEntity>(x => x.RelatedId)
                .Index(x => x.Username)
                .DocumentAlias("alias_name");
        });

        var sql = TheStore.Storage.Database.ToDatabaseScript();
        sql.ShouldContain("ADD CONSTRAINT mt_doc_alias_name_related_id_fkey");

        await TheStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

    }
}

public class OriginalNameEntity
{
    public Guid Id { get; set; }
    public Guid RelatedId { get; set; }

    public string Username { get; set; }
}

public class RelatedEntity
{
    public Guid Id { get; set; }
}