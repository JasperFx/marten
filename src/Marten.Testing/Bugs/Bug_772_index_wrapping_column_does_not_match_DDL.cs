using Marten.Schema;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_772_index_wrapping_column_does_not_match_DDL : IntegratedFixture
    {
        [Fact] // Control
        public void index_with_no_expression_should_match_DDL()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Company>()
                    .Duplicate(c => c.Name);
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            theStore.Schema.AssertDatabaseMatchesConfiguration();
        }

        [Fact] // Control
        public void index_with_expression_not_wrapping_column_should_match_DDL()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Company>()
                    .Duplicate(c => c.Name, pgType:"jsonb", configure:id =>
                    {
                        id.Method = IndexMethod.gin;
                        id.Expression = "? jsonb_path_ops";
                    });
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            theStore.Schema.AssertDatabaseMatchesConfiguration();
        }

        [Fact] // Experiment, passed
        public void index_with_expression_wrapping_column_should_match_DDL_if_DDL_not_reformatted()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Company>()
                    .Duplicate(c => c.Name, configure: id =>
                    {
                        // The DDL for this expression is not reformatted,
                        // and so the index definition matches the DDL.
                        id.Expression = "lower((?)::text)";
                        id.IndexName = id.IndexName + "_lower";
                    });
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            theStore.Schema.AssertDatabaseMatchesConfiguration();
        }

        //[Fact] // Experiment, failed
        public void index_with_expression_wrapping_column_should_match_DDL()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Company>()
                    .Duplicate(c => c.Name, configure: id =>
                    {
                        // The DDL for this expression is reformatted,
                        // and so I'm not certain there is a simple
                        // way to get the formats to match.
                        id.Expression = "lower(?)";
                        id.IndexName = id.IndexName + "_lower";
                    });
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            theStore.Schema.AssertDatabaseMatchesConfiguration();
        }
    }
}
