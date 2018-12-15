using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1151_assert_db_matches_config_exception : IntegratedFixture
    {
        [Fact]
        public void check_assert_db_matches_config_for_doc_with_pg_keyword_prop()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Schema.For<Bug1151>()
                    .Duplicate(c => c.Trim);
            });
            
            theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            theStore.Schema.AssertDatabaseMatchesConfiguration();
        }
    }
    
    class Bug1151
    {
        public string Id { get; set; }
        // trim is a PostgreSQL keyword
        public string Trim { get; set; }
    }
}