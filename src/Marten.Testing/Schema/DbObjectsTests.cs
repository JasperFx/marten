using System;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Schema
{
    public class DbObjectsTests : IntegratedFixture
    {
        private readonly ITestOutputHelper _output;

        public DbObjectsTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void can_fetch_indexes_for_a_table_in_public()
        {
            var store1 = TestingDocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "other";
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
            });

            store1.Schema.EnsureStorageExists(typeof(User));


            var store2 = TestingDocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.FirstName);
            });

            store2.Schema.EnsureStorageExists(typeof(User));

            var indices = store2.Schema.DbObjects.AllIndexes();

            indices.Any(x => Equals(x.Table, store1.Schema.MappingFor(typeof(User)).Table))
                .ShouldBeTrue();

            indices.Any(x => Equals(x.Table, store2.Schema.MappingFor(typeof(User)).Table))
                .ShouldBeTrue();


        }

        [Fact]
        public void can_fetch_the_function_ddl()
        {
            var store1 = TestingDocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "other";
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
            });

            store1.Schema.EnsureStorageExists(typeof(User));

            var upsert = store1.Schema.MappingFor(typeof(User)).As<DocumentMapping>().UpsertFunction;

            var ddl = store1.Schema.DbObjects.DefinitionForFunction(upsert);

            ddl.ShouldContain("mt_doc_user");
        }


        [Fact]
        public void can_fetch_schema_objects_for_a_document_type_in_another_schema()
        {
            var store1 = TestingDocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "other";
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
            });

            store1.Schema.EnsureStorageExists(typeof(User));


            var objects = store1.Schema.DbObjects.FindSchemaObjects(store1.Schema.MappingFor(typeof(User)).As<DocumentMapping>());



            objects.Table.Columns.OrderBy(x => x.Name).Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("data", "id", "internal", DocumentMapping.DotNetTypeColumn, DocumentMapping.LastModifiedColumn, DocumentMapping.VersionColumn, "user_name");

            objects.UpsertFunction.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_user");


            objects.ActualIndices.Select(x => x.Value).OrderBy(x => x.Name).Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("mt_doc_user_idx_internal", "mt_doc_user_idx_user_name");
        }

        [Fact]
        public void can_fetch_schema_objects_for_a_document_type_in_the_default_schema()
        {
            var store1 = TestingDocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "public";
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
            });

            store1.Schema.EnsureStorageExists(typeof(User));


            var objects = store1.Schema.DbObjects.FindSchemaObjects(store1.Schema.MappingFor(typeof(User)).As<DocumentMapping>());


            objects.Table.Columns.OrderBy(x => x.Name).Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("data", "id", "internal", DocumentMapping.DotNetTypeColumn, DocumentMapping.LastModifiedColumn, DocumentMapping.VersionColumn, "user_name");

            objects.UpsertFunction.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_user");


            objects.ActualIndices.Select(x => x.Value).OrderBy(x => x.Name).Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("mt_doc_user_idx_internal", "mt_doc_user_idx_user_name");
        }

        [Fact]
        public void fetch_schema_objects_for_a_document_type_that_has_not_been_created_yet()
        {
            var store1 = TestingDocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "public";
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
            });


            var objects = store1.Schema.DbObjects.FindSchemaObjects(store1.Schema.MappingFor(typeof(User)).As<DocumentMapping>());

            objects.HasNone().ShouldBeTrue();
            objects.Table.ShouldBeNull();
            objects.ActualIndices.Any().ShouldBeFalse();
            objects.UpsertFunction.ShouldBeNull();

        }
    }
}