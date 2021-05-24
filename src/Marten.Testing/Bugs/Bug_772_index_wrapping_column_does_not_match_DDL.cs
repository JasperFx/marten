using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Weasel.Postgresql.Tables;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_772_index_wrapping_column_does_not_match_DDL: BugIntegrationContext
    {
        [Fact] // Control
        public async Task index_with_no_expression_should_match_DDL()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Company>()
                    .Duplicate(c => c.Name);
            });

            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            await theStore.Schema.AssertDatabaseMatchesConfiguration();
        }

        [Fact] // Control
        public async Task index_with_expression_not_wrapping_column_should_match_DDL()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Company>()
                    .Duplicate(c => c.Name, pgType: "jsonb", configure: id =>
                      {
                          id.ToGinWithJsonbPathOps();
                      });
            });

            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            await theStore.Schema.AssertDatabaseMatchesConfiguration();
        }

        [Fact] // Experiment, passed
        public async Task index_with_expression_wrapping_column_should_match_DDL_if_DDL_not_reformatted()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Company>()
                    .Duplicate(c => c.Name, configure: id =>
                    {
                        // The DDL for this expression is not reformatted,
                        // and so the index definition matches the DDL.
                        id.Mask = "lower(?)";
                        id.Name = id.Name + "_lower";
                    });
            });

            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            await theStore.Schema.AssertDatabaseMatchesConfiguration();
        }


    }
}
