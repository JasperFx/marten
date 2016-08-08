using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Schema
{

    public class when_doing_a_schema_diff_with_no_changes_95_style
    {
        private DocumentStore store2;
        private DocumentMapping mapping;
        private SchemaDiff diff;

        public when_doing_a_schema_diff_with_no_changes_95_style()
        {
            var store1 = TestingDocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal);
            });

            store1.Schema.EnsureStorageExists(typeof(User));


            // Don't use TestingDocumentStore because it cleans everything upfront.
            store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal);
            });

            mapping = store2.Schema.MappingFor(typeof(User)).As<DocumentMapping>();
            diff = mapping.SchemaObjects.As<DocumentSchemaObjects>().CreateSchemaDiff(store2.Schema);
        }

        [Fact]
        public void all_missing_should_be_false()
        {
            diff.AllMissing.ShouldBeFalse();
        }

        [Fact]
        public void function_should_not_have_changed()
        {
            diff.FunctionDiff.HasChanged.ShouldBeFalse();
        }

        [Fact]
        public void no_differences()
        {
            diff.HasDifferences().ShouldBeFalse();
        }

        [Fact]
        public void no_index_changes()
        {
            diff.IndexChanges.Any().ShouldBeFalse();
        }

        [Fact]
        public void no_table_diff()
        {
            diff.TableDiff.Matches.ShouldBeTrue();
        }
    }



    public class SchemaDiff_Generation_Tests
    {
        [Fact]
        public void no_changes_in_upsert_function_95_style()
        {
            var store1 = TestingDocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal);
            });

            store1.Advanced.Clean.CompletelyRemoveAll();

            store1.Schema.EnsureStorageExists(typeof(User));


            // Don't use TestingDocumentStore because it cleans everything upfront.
            var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal);
            });

            var mapping = store2.Schema.MappingFor(typeof(User)).As<DocumentMapping>();
            var diff = mapping.SchemaObjects.As<DocumentSchemaObjects>().CreateSchemaDiff(store2.Schema);

            diff.AllMissing.ShouldBeFalse();

            diff.FunctionDiff.HasChanged.ShouldBeFalse();

            diff.HasDifferences().ShouldBeFalse();


        }


        [Fact]
        public void detect_change_in_upsert_function_95_style()
        {
            var store1 = TestingDocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                //_.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal);
            });

            store1.Schema.EnsureStorageExists(typeof(User));


            // Don't use TestingDocumentStore because it cleans everything upfront.
            var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal);
            });

            var mapping = store2.Schema.MappingFor(typeof(User)).As<DocumentMapping>();
            var diff = mapping.SchemaObjects.As<DocumentSchemaObjects>().CreateSchemaDiff(store2.Schema);

            diff.AllMissing.ShouldBeFalse();

            diff.FunctionDiff.HasChanged.ShouldBeTrue();

        }

        [Fact]
        public void build_function_if_it_does_not_exist()
        {
            var store1 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal);
            });


            var documentMapping = store1.Schema.MappingFor(typeof(User)).As<DocumentMapping>();
            using (var conn = store1.Advanced.OpenConnection())
            {
                documentMapping.SchemaObjects.As<DocumentSchemaObjects>().RemoveUpsertFunction(conn);
            }

            store1.Schema.DbObjects.FindSchemaObjects(documentMapping)
                .Function.ShouldBeNull();

            // Don't use TestingDocumentStore because it cleans everything upfront.
            var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal);
            });



            store2.Schema.EnsureStorageExists(typeof(User));

            var mapping = store2.Schema.MappingFor(typeof(User)).As<DocumentMapping>();
            var objects = store2.Schema.DbObjects.FindSchemaObjects(mapping);

            objects.Function.ShouldNotBeNull();

        }

        

        [Fact]
        public void will_overwrite_upsert_function_when_it_has_changed()
        {
            var store1 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal).UseOptimisticConcurrency(true);
            });

            store1.Advanced.Clean.CompletelyRemoveAll();
            store1.Schema.EnsureStorageExists(typeof(User));




            // Don't use TestingDocumentStore because it cleans everything upfront.
            var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal);
            });



            store2.Schema.EnsureStorageExists(typeof(User));

            var mapping = store2.Schema.MappingFor(typeof(User)).As<DocumentMapping>();
            var schemaDiff = mapping.SchemaObjects.As<DocumentSchemaObjects>().CreateSchemaDiff(store2.Schema);
            schemaDiff.FunctionDiff.HasChanged.ShouldBeFalse();


            schemaDiff.IndexChanges.Any().ShouldBeFalse();
        }

        [Fact]
        public void will_write_missing_indexes()
        {
            var store1 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                //_.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal);
            });

            store1.Advanced.Clean.CompletelyRemoveAll();
            store1.Schema.EnsureStorageExists(typeof(User));




            // Don't use TestingDocumentStore because it cleans everything upfront.
            var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal);
            });

            var mapping = store2.Schema.MappingFor(typeof(User)).As<DocumentMapping>();
            var schemaDiff = mapping.SchemaObjects.As<DocumentSchemaObjects>().CreateSchemaDiff(store2.Schema);
            schemaDiff.IndexChanges.Count.ShouldBe(2);

            store2.Schema.EnsureStorageExists(typeof(User));

            var schemaDiff2 = mapping.SchemaObjects.As<DocumentSchemaObjects>().CreateSchemaDiff(store2.Schema);
            schemaDiff2.HasDifferences().ShouldBeFalse();

            schemaDiff2.IndexChanges.Any().ShouldBeFalse();


            var objects = store2.Schema.DbObjects.FindSchemaObjects(mapping);
            objects.ActualIndices.Select(x => x.Key).OrderBy(x => x)
                .ShouldHaveTheSameElementsAs("mt_doc_user_idx_internal", "mt_doc_user_idx_user_name");

        }


        [Fact]
        public void will_write_changed_indexes()
        {
            var store1 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Duplicate(x => x.UserName).Duplicate(x => x.Internal);
            });

            store1.Advanced.Clean.CompletelyRemoveAll();
            store1.Schema.EnsureStorageExists(typeof(User));




            // Don't use TestingDocumentStore because it cleans everything upfront.
            var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Duplicate(x => x.UserName)
                    .Duplicate(x => x.Internal, configure: i => i.Method = IndexMethod.hash);
            });

            var mapping = store2.Schema.MappingFor(typeof(User)).As<DocumentMapping>();
            var schemaDiff = mapping.SchemaObjects.As<DocumentSchemaObjects>().CreateSchemaDiff(store2.Schema);
            schemaDiff.IndexChanges.Count.ShouldBe(1);

            store2.Schema.EnsureStorageExists(typeof(User));

            var schemaDiff2 = mapping.SchemaObjects.As<DocumentSchemaObjects>().CreateSchemaDiff(store2.Schema);
            schemaDiff2.HasDifferences().ShouldBeFalse();

            schemaDiff2.IndexChanges.Any().ShouldBeFalse();

        }
    }
}