using Baseline;
using Marten.Schema;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Schema
{
    public class SchemaDiff_Generation_Tests
    {
        [Fact]
        public void no_changes_in_upsert_function_94_style()
        {
            var store1 = TestingDocumentStore.For(_ =>
            {
                _.UpsertType = PostgresUpsertType.Legacy;
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
            });

            store1.Schema.EnsureStorageExists(typeof(User));


            // Don't use TestingDocumentStore because it cleans everything upfront.
            var store2 = DocumentStore.For(_ =>
            {
                _.UpsertType = PostgresUpsertType.Legacy;
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
            });

            var mapping = store2.Schema.MappingFor(typeof(User)).As<DocumentMapping>();
            var diff = mapping.CreateSchemaDiff(store2.Schema);

            diff.AllMissing.ShouldBeFalse();

            diff.HasFunctionChanged().ShouldBeFalse();

            diff.HasDifferences().ShouldBeFalse();

            
        }

        [Fact]
        public void no_changes_in_upsert_function_95_style()
        {
            var store1 = TestingDocumentStore.For(_ =>
            {
                _.UpsertType = PostgresUpsertType.Standard;
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
            });

            store1.Schema.EnsureStorageExists(typeof(User));


            // Don't use TestingDocumentStore because it cleans everything upfront.
            var store2 = DocumentStore.For(_ =>
            {
                _.UpsertType = PostgresUpsertType.Standard;
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
            });

            var mapping = store2.Schema.MappingFor(typeof(User)).As<DocumentMapping>();
            var diff = mapping.CreateSchemaDiff(store2.Schema);

            diff.AllMissing.ShouldBeFalse();

            diff.HasFunctionChanged().ShouldBeFalse();

            diff.HasDifferences().ShouldBeFalse();


        }
    }
}