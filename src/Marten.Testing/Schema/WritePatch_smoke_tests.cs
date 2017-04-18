using System;
using Baseline;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Events;
using Xunit;

namespace Marten.Testing.Schema
{
    public class WritePatch_smoke_tests : IntegratedFixture
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

        [Fact]
        public void can_create_patch_for_a_single_document_type()
        {
            StoreOptions(_ =>
            {
                // This is enough to tell Marten that the User
                // document is persisted and needs schema objects
                _.Schema.For<User>();
            });

            var patch = theStore.Schema.ToPatch(typeof(User));

            patch.UpdateDDL.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_user");
            patch.UpdateDDL.ShouldContain("CREATE TABLE public.mt_doc_user");
            patch.RollbackDDL.ShouldContain("drop table if exists public.mt_doc_user cascade;");

            var file = AppContext.BaseDirectory.AppendPath("bin", "update_users.sql");
            patch.WriteUpdateFile(file);

            var text = new FileSystem().ReadStringFromFile(file);

            text.ShouldContain("DO LANGUAGE plpgsql $tran$");
            text.ShouldContain("$tran$;");
        }

        [Fact]
        public void can_do_schema_validation_negative_case_with_detected_changes()
        {
            theStore.DefaultTenant.EnsureStorageExists(typeof(User));
            theStore.DefaultTenant.EnsureStorageExists(typeof(Target));

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>().Duplicate(x => x.UserName);
                _.Schema.For<Target>();
            }))
            {
                Exception<SchemaValidationException>.ShouldBeThrownBy(
                    () => { store.Schema.AssertDatabaseMatchesConfiguration(); }).Message.ShouldContain("user_name");
            }
        }

        [Fact]
        public void can_do_schema_validation_with_no_detected_changes()
        {
            theStore.DefaultTenant.EnsureStorageExists(typeof(User));
            theStore.DefaultTenant.EnsureStorageExists(typeof(Target));

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

        [Fact]
        public void writes_both_the_update_and_rollback_files()
        {
            StoreOptions(_ =>
            {
                // This is enough to tell Marten that the User
                // document is persisted and needs schema objects
                _.Schema.For<User>();

                // Lets Marten know that the event store is active
                _.Events.AddEventType(typeof(MembersJoined));
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
    }
}