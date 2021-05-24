using System;
using System.Threading.Tasks;

using Bug1822;

using Marten.Schema;
using Marten.Testing.Harness;

using Weasel.Postgresql;

using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1822_recreating_index_fails_if_there_are_more_than_one_change: BugIntegrationContext
    {
        [Fact]
        public async Task should_be_able_to_migrate_schema_with_multiple_schema_changes()
        {
            using var documentStore = SeparateStore(x =>
            {
                x.AutoCreateSchemaObjects = AutoCreate.All;
                x.Schema.For<TestDocument>()
                    .Index(y => y.FirstName, index => index.Casing = ComputedIndex.Casings.Lower)
                    .Index(y => y.LastName, index => index.Casing = ComputedIndex.Casings.Lower);
            });

            await documentStore.Advanced.Clean.CompletelyRemoveAllAsync();
            await documentStore.Schema.ApplyAllConfiguredChangesToDatabase(AutoCreate.All);

            using var secondDocumentStore = SeparateStore(x =>
            {
                x.AutoCreateSchemaObjects = AutoCreate.All;
                x.Schema.For<TestDocument>()
                    .Index(y => y.FirstName, index => index.Casing = ComputedIndex.Casings.Upper)
                    .Index(y => y.LastName, index => index.Casing = ComputedIndex.Casings.Upper);
            });

            await documentStore.Schema.ApplyAllConfiguredChangesToDatabase(AutoCreate.All);
        }
    }
}

namespace Bug1822
{
    public class TestDocument
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
    }
}
