using System.Threading.Tasks;
using Marten.Testing.Harness;
using Weasel.Core;
using Xunit;

namespace CoreTests.Bugs
{
    public class Bug_1151_assert_db_matches_config_exception: BugIntegrationContext
    {
        [Fact]
        public async Task check_assert_db_matches_config_for_doc_with_pg_keyword_prop()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Schema.For<Bug1151>()
                    .Duplicate(c => c.Trim);
            });

            await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
        }

    }

    internal class Bug1151
    {
        public string Id { get; set; }

        // trim is a PostgreSQL keyword
        public string Trim { get; set; }
    }
}
