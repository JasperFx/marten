using System;
using System.Linq;
using Baseline;
using Marten.Events;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class WritePatch_smoke_tests : DestructiveIntegrationContext
    {
        private void configure()
        {
            // SAMPLE: configure-document-types-upfront
            var store = DocumentStore.For(_ =>
            {
                // This is enough to tell Marten that the User
                // document is persisted and needs schema objects
                _.Schema.For<User>();

                // Lets Marten know that the event store is active
                _.Events.AddEventType(typeof(MembersJoined));

                // Tell Marten about all the javascript functions
                _.Transforms.LoadFile("default_username.js");
            });
            // ENDSAMPLE

            // SAMPLE: WritePatch
            store.Schema.WritePatch("1.initial.sql");
            // ENDSAMPLE

            // SAMPLE: ApplyAllConfiguredChangesToDatabase
            store.Schema.ApplyAllConfiguredChangesToDatabase();
            // ENDSAMPLE

            // SAMPLE: AssertDatabaseMatchesConfiguration
            store.Schema.AssertDatabaseMatchesConfiguration();
            // ENDSAMPLE
            store.Dispose();
        }

        //[Fact] //-- flakey on ci
        public void can_create_patch_for_a_single_document_type()
        {
            StoreOptions(_ =>
            {
                // This is enough to tell Marten that the User
                // document is persisted and needs schema objects
                _.Schema.For<User>();
            });

            var patch = theStore.Schema.ToPatch(typeof(User));

            SpecificationExtensions.ShouldContain(patch.UpdateDDL, "CREATE OR REPLACE FUNCTION public.mt_upsert_user");
            SpecificationExtensions.ShouldContain(patch.UpdateDDL, "CREATE TABLE public.mt_doc_user");
            SpecificationExtensions.ShouldContain(patch.RollbackDDL, "drop table if exists public.mt_doc_user cascade;");

            var file = AppContext.BaseDirectory.AppendPath("bin", "update_users.sql");
            patch.WriteUpdateFile(file);

            var text = new FileSystem().ReadStringFromFile(file);

            SpecificationExtensions.ShouldContain(text, "DO LANGUAGE plpgsql $tran$");
            SpecificationExtensions.ShouldContain(text, "$tran$;");
        }

        //[Fact] //-- flakey on ci
        public void can_do_schema_validation_negative_case_with_detected_changes()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
            theStore.Tenancy.Default.EnsureStorageExists(typeof(Target));

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>().Duplicate(x => x.UserName);
                _.Schema.For<Target>();
            }))
            {
                SpecificationExtensions.ShouldContain(Exception<SchemaValidationException>.ShouldBeThrownBy(
                    () => { store.Schema.AssertDatabaseMatchesConfiguration(); }).Message, "user_name");
            }
        }

        //[Fact] //-- flakey on ci
        public void can_do_schema_validation_with_no_detected_changes()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
            theStore.Tenancy.Default.EnsureStorageExists(typeof(Target));

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>();
                _.Schema.For<Target>();
            }))
            {
                store.Schema.AssertDatabaseMatchesConfiguration();
            }
        }

        [Fact] // -- flakey on ci
        public void can_do_schema_validation_with_no_detected_changes_on_event_store()
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

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();



            using (var store = DocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = schemaName;
                _.Connection(ConnectionSource.ConnectionString);
                _.Events.AddEventType(typeof(MembersJoined));
            }))
            {
                store.Schema.AssertDatabaseMatchesConfiguration();
            }
        }

        //[Fact] // -- flakey on ci
        public void writes_both_the_update_and_rollback_files()
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

            // SAMPLE: write-patch
            // Write the patch SQL file to the @"bin\patches" directory
            theStore.Schema.WritePatch(directory.AppendPath("1.initial.sql"));
            // ENDSAMPLE

            fileSystem.FileExists(directory.AppendPath("1.initial.sql"));
            fileSystem.FileExists(directory.AppendPath("1.initial.drop.sql"));
        }

        //[Fact] // -- flakey on ci
        public void writepatch_writes_patch_schema_when_autocreate_none()
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

            // SAMPLE: write-patch
            // Write the patch SQL file to the @"bin\patches" directory
            theStore.Schema.WritePatch(directory.AppendPath("1.initial.sql"));
            // ENDSAMPLE

            fileSystem.FileExists(directory.AppendPath("1.initial.sql"));
            fileSystem.FileExists(directory.AppendPath("1.initial.drop.sql"));

            var patchSql = fileSystem.ReadStringFromFile(directory.AppendPath("1.initial.sql"));

            SpecificationExtensions.ShouldContain(patchSql, "CREATE TABLE public.mt_doc_user");
        }

        public WritePatch_smoke_tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
