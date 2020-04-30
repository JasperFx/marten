using Marten.Schema;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_983_autocreate_none_is_disabling_schema_validation: IntegrationContext
    {
        public class Document
        {
            public int Id { get; set; }
        }

        [Fact]
        public void should_be_validating_the_new_doc_does_not_exist()
        {
            StoreOptions(cfg =>
            {
                cfg.Schema.For<Document>();

                cfg.AutoCreateSchemaObjects = AutoCreate.None;
            });

            Exception<SchemaValidationException>.ShouldBeThrownBy(() =>
            {
                theStore.Schema.AssertDatabaseMatchesConfiguration();
            });
        }

        public Bug_983_autocreate_none_is_disabling_schema_validation(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
