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
        public void detect_change_in_upsert_function_94_style()
        {
            var store1 = TestingDocumentStore.For(_ =>
            {
                _.UpsertType = PostgresUpsertType.Legacy;
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                //_.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
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

            diff.HasFunctionChanged().ShouldBeTrue();

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


        [Fact]
        public void detect_change_in_upsert_function_95_style()
        {
            var store1 = TestingDocumentStore.For(_ =>
            {
                _.UpsertType = PostgresUpsertType.Standard;
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                //_.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
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

            diff.HasFunctionChanged().ShouldBeTrue();

        }

        [Fact]
        public void build_function_if_it_does_not_exist()
        {
            var store1 = DocumentStore.For(_ =>
            {
                _.UpsertType = PostgresUpsertType.Standard;
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
            });


            var documentMapping = store1.Schema.MappingFor(typeof(User)).As<DocumentMapping>();
            using (var conn = store1.Advanced.OpenConnection())
            {
                documentMapping.RemoveUpsertFunction(conn);
            }

            store1.Schema.DbObjects.FindSchemaObjects(documentMapping)
                .UpsertFunction.ShouldBeNull();

            // Don't use TestingDocumentStore because it cleans everything upfront.
            var store2 = DocumentStore.For(_ =>
            {
                _.UpsertType = PostgresUpsertType.Standard;
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
            });



            store2.Schema.EnsureStorageExists(typeof(User));

            var mapping = store2.Schema.MappingFor(typeof(User)).As<DocumentMapping>();
            var objects = store2.Schema.DbObjects.FindSchemaObjects(mapping);

            objects.UpsertFunction.ShouldNotBeNull();

        }

        

        [Fact]
        public void will_overwrite_upsert_function_when_it_has_changed()
        {
            var store1 = DocumentStore.For(_ =>
            {
                _.UpsertType = PostgresUpsertType.Standard;
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
            });


            // Modifying the Upsert
            var documentMapping = store1.Schema.MappingFor(typeof(User)).As<DocumentMapping>();
            using (var conn = store1.Advanced.OpenConnection())
            {
                var sql =
                    "CREATE OR REPLACE FUNCTION public.mt_upsert_user(arg_internal boolean, arg_user_name varchar, doc JSONB, docId uuid) RETURNS void LANGUAGE plpgsql AS $function$ BEGIN LOCK TABLE public.mt_doc_user IN SHARE ROW EXCLUSIVE MODE;  WITH upsert AS (UPDATE public.mt_doc_user SET \"user_name\" = arg_user_name, \"data\" = doc WHERE id=docId RETURNING *)   INSERT INTO public.mt_doc_user (\"internal\", \"user_name\", \"data\", \"id\")  SELECT arg_internal, arg_user_name, doc, docId WHERE NOT EXISTS (SELECT * FROM upsert); END; $function$";

                conn.Execute(cmd => cmd.CommandText = sql);
            }

            documentMapping.CreateSchemaDiff(store1.Schema).HasFunctionChanged()
                .ShouldBeTrue();


            // Don't use TestingDocumentStore because it cleans everything upfront.
            var store2 = DocumentStore.For(_ =>
            {
                _.UpsertType = PostgresUpsertType.Standard;
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
                _.Schema.For<User>().Searchable(x => x.UserName).Searchable(x => x.Internal);
            });



            store2.Schema.EnsureStorageExists(typeof(User));

            var mapping = store2.Schema.MappingFor(typeof(User)).As<DocumentMapping>();
            mapping.CreateSchemaDiff(store2.Schema).HasFunctionChanged().ShouldBeFalse();
        }
    }
}