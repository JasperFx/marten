using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Bugs
{
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

            await Exception<SchemaValidationException>.ShouldBeThrownByAsync(() =>
            {
                return theStore.Schema.AssertDatabaseMatchesConfiguration();
            });
        }

    }
}
