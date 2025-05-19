using System;
using System.Threading.Tasks;
using JasperFx;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Core.Migrations;
using Xunit;

namespace CoreTests.Bugs;

[Obsolete("This should be in Weasel")]
public class Bug_983_autocreate_none_is_disabling_schema_validation: BugIntegrationContext
{
    public class Document
    {
        public int Id { get; set; }
    }

    [Fact]
    public async Task should_be_validating_the_new_doc_does_not_exist()
    {
        StoreOptions(cfg =>
        {
            cfg.Schema.For<Document>();

            cfg.AutoCreateSchemaObjects = AutoCreate.None;
        });

        await Should.ThrowAsync<DatabaseValidationException>(async () =>
        {
            await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
        });
    }

}
