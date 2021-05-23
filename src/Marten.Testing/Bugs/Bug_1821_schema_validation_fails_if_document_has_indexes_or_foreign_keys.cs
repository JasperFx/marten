using System;
using System.Threading.Tasks;

using Bug1821;

using Marten.Schema;
using Marten.Testing.Harness;

using Weasel.Postgresql;

using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1821_schema_validation_fails_if_document_has_indexes_or_foreign_keys: BugIntegrationContext
    {
        [Theory]
        [InlineData(ComputedIndex.Casings.Default)]
        [InlineData(ComputedIndex.Casings.Lower)]
        [InlineData(ComputedIndex.Casings.Upper)]
        public async Task schema_validation_should_succeed_when_document_has_index(ComputedIndex.Casings casing)
        {
            using var documentStore = SeparateStore(x =>
            {
                x.AutoCreateSchemaObjects = AutoCreate.All;
                x.Schema.For<TestDocument>().Index(y => y.Name, c => c.Casing = casing);
            });

            await documentStore.Advanced.Clean.CompletelyRemoveAllAsync();
            await documentStore.Schema.ApplyAllConfiguredChangesToDatabase(AutoCreate.All);

            await documentStore.Schema.AssertDatabaseMatchesConfiguration();
        }

        [Fact]
        public async Task schema_validation_should_succeed_when_document_has_foreign_key()
        {
            using var documentStore = SeparateStore(x =>
            {
                x.AutoCreateSchemaObjects = AutoCreate.All;
                x.Schema.For<TestDocument>().ForeignKey<TestDocument>(y => y.ParentId);
            });

            await documentStore.Advanced.Clean.CompletelyRemoveAllAsync();
            await documentStore.Schema.ApplyAllConfiguredChangesToDatabase(AutoCreate.All);

            await documentStore.Schema.AssertDatabaseMatchesConfiguration();
        }
    }
}

namespace Bug1821
{
    public class TestDocument
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        public Guid? ParentId { get; set; }
    }
}
