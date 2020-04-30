using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1002_new_duplicate_field_write_patch_syntax_error: IntegrationContext
    {
        [Fact]
        public void update_patch_should_not_contain_double_semicolon()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.DdlRules.TableCreation = CreationStyle.CreateIfNotExists;
                _.Schema.For<Bug_1002>();
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Bug_1002>()
                .Duplicate(x => x.Name); // add a new duplicate column
            }))
            {
                // check if the patch script does not contain double colon between alter and update statements
                store.Schema.ToPatch().UpdateDDL.ShouldNotContain(";;");
            }
        }

        public Bug_1002_new_duplicate_field_write_patch_syntax_error(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }

    public class Bug_1002
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }
}
