using System;
using System.Threading.Tasks;
using Baseline;
using Marten.Exceptions;
using Marten.Schema.Testing.Documents;
using Marten.Testing.Harness;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Schema.Testing
{
    public class WritePatch_smoke_tests : IntegrationContext
    {
        private async Task configure()
        {
            #region sample_configure-document-types-upfront
            var store = DocumentStore.For(_ =>
            {
                // This is enough to tell Marten that the User
                // document is persisted and needs schema objects
                _.Schema.For<User>();

                // Lets Marten know that the event store is active
                _.Events.AddEventType(typeof(MembersJoined));
            });
            #endregion sample_configure-document-types-upfront

            #region sample_WritePatch
            await store.Schema.WriteMigrationFile("1.initial.sql");
            #endregion sample_WritePatch

            #region sample_ApplyAllConfiguredChangesToDatabase
            await store.Schema.ApplyAllConfiguredChangesToDatabase();
            #endregion sample_ApplyAllConfiguredChangesToDatabase

            #region sample_AssertDatabaseMatchesConfiguration
            await store.Schema.AssertDatabaseMatchesConfiguration();
            #endregion sample_AssertDatabaseMatchesConfiguration
            store.Dispose();
        }

        [Fact(Skip = "flakey on ci")]
        public async Task can_create_patch_for_a_single_document_type()
        {
            StoreOptions(_ =>
            {
                // This is enough to tell Marten that the User
                // document is persisted and needs schema objects
                _.Schema.For<User>();
            });

            var patch = await theStore.Schema.CreateMigration(typeof(User));

            patch.UpdateSql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_user");
            patch.UpdateSql.ShouldContain("CREATE TABLE public.mt_doc_user");
            patch.RollbackSql.ShouldContain("drop table if exists public.mt_doc_user cascade;");

            var file = AppContext.BaseDirectory.AppendPath("bin", "update_users.sql");
            new DdlRules().WriteTemplatedFile(file, (r, w) => patch.WriteAllUpdates(w, r, AutoCreate.CreateOrUpdate));

            var text = new FileSystem().ReadStringFromFile(file);

            text.ShouldContain("DO LANGUAGE plpgsql $tran$");
            text.ShouldContain("$tran$;");
        }

        [Fact(Skip = "flakey on ci")]
        public async Task can_do_schema_validation_negative_case_with_detected_changes()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
            theStore.Tenancy.Default.EnsureStorageExists(typeof(Target));

            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>().Duplicate(x => x.UserName);
                _.Schema.For<Target>();
            }))
            {
                var ex = await Exception<SchemaValidationException>.ShouldBeThrownByAsync(async
                    () =>
                {
                    await store.Schema.AssertDatabaseMatchesConfiguration();
                });



                ex.Message.ShouldContain("user_name");
            }
        }

        [Fact(Skip = "flakey on ci")]
        public async Task can_do_schema_validation_with_no_detected_changes()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
            theStore.Tenancy.Default.EnsureStorageExists(typeof(Target));

            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            using var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>();
                _.Schema.For<Target>();
            });
            await store.Schema.AssertDatabaseMatchesConfiguration();
        }

        [Fact] // -- flakey on ci
        public async Task can_do_schema_validation_with_no_detected_changes_on_event_store()
        {
            /*
            var system = new FileSystem();

            var expected = system.ReadStringFromFile("c:\\expected.sql").ReadLines().ToArray();
            var actual = system.ReadStringFromFile("c:\\actual.sql").ReadLines().ToArray();


            for (int i = 0; i < expected.Length; i++)
            {
                Console.WriteLine(i);
                actual[i].ShouldBe(expected[i]);
            }
            */



            var schemaName = StoreOptions(_ =>
            {
                _.Events.AddEventType(typeof(MembersJoined));
            });

            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();


            using var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Events.AddEventType(typeof(MembersJoined));
            });

            await store.Schema.AssertDatabaseMatchesConfiguration();
        }

        [Fact(Skip = "flakey on ci")]
        public async Task writes_both_the_update_and_rollback_files()
        {
            StoreOptions(_ =>
            {
                // This is enough to tell Marten that the User
                // document is persisted and needs schema objects
                _.Schema.For<User>();

                // Lets Marten know that the event store is active
                _.Events.AddEventType(typeof(MembersJoined));

                _.AutoCreateSchemaObjects = AutoCreate.CreateOnly;
            });

            var directory = AppContext.BaseDirectory.AppendPath("bin", "patches");

            var fileSystem = new FileSystem();
            fileSystem.DeleteDirectory(directory);
            fileSystem.CreateDirectory(directory);

            #region sample_write-patch
            // Write the patch SQL file to the @"bin\patches" directory
            await theStore.Schema.WriteMigrationFile(directory.AppendPath("1.initial.sql"));
            #endregion sample_write-patch

            fileSystem.FileExists(directory.AppendPath("1.initial.sql"));
            fileSystem.FileExists(directory.AppendPath("1.initial.drop.sql"));
        }

        [Fact(Skip = "flakey on ci")]
        public async Task writepatch_writes_patch_schema_when_autocreate_none()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>();
                _.AutoCreateSchemaObjects = AutoCreate.None;
            });

            var directory = AppContext.BaseDirectory.AppendPath("bin", "patches");

            var fileSystem = new FileSystem();
            fileSystem.DeleteDirectory(directory);
            fileSystem.CreateDirectory(directory);

            #region sample_write-patch
            // Write the patch SQL file to the @"bin\patches" directory
            await theStore.Schema.WriteMigrationFile(directory.AppendPath("1.initial.sql"));
            #endregion sample_write-patch

            fileSystem.FileExists(directory.AppendPath("1.initial.sql"));
            fileSystem.FileExists(directory.AppendPath("1.initial.drop.sql"));

            var patchSql = fileSystem.ReadStringFromFile(directory.AppendPath("1.initial.sql"));

            patchSql.ShouldContain("CREATE TABLE public.mt_doc_user");
        }

    }
}
