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
        [Fact]
        public async Task schema_validation_should_succeed_when_document_has_index()
        {
            using var documentStore = SeparateStore(x =>
            {
                x.AutoCreateSchemaObjects = AutoCreate.All;
                x.Schema.For<TestDocument>()
                    .Index(y => y.Name);
            });

            await documentStore.Advanced.Clean.CompletelyRemoveAllAsync();
            await documentStore.Schema.ApplyAllConfiguredChangesToDatabase(AutoCreate.All);

            await documentStore.Schema.AssertDatabaseMatchesConfiguration();
        }

        [Fact]
        public async Task schema_validation_should_succeed_when_document_has_index_to_upper()
        {
            using var documentStore = SeparateStore(x =>
            {
                x.AutoCreateSchemaObjects = AutoCreate.All;
                x.Schema.For<TestDocument>()
                    .Index(y => y.Name, i => i.Casing = ComputedIndex.Casings.Upper);
            });

            await documentStore.Advanced.Clean.CompletelyRemoveAllAsync();
            await documentStore.Schema.ApplyAllConfiguredChangesToDatabase(AutoCreate.All);

            await documentStore.Schema.AssertDatabaseMatchesConfiguration();
        }

        [Fact]
        public async Task schema_validation_should_succeed_when_document_has_index_to_lower()
        {
            using var documentStore = SeparateStore(x =>
            {
                x.AutoCreateSchemaObjects = AutoCreate.All;
                x.Schema.For<TestDocument>()
                    .Index(y => y.Name, i => i.Casing = ComputedIndex.Casings.Lower);
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
                x.Schema.For<TestDocument>()
                    .ForeignKey<TestDocument>(y => y.ParentId);
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
