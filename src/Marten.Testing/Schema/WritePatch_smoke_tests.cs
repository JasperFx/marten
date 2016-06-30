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

            var fileSystem = new FileSystem();
            fileSystem.DeleteDirectory(@"bin\patches");
            fileSystem.CreateDirectory(@"bin\patches");

            // SAMPLE: write-patch
            // Write the patch SQL file to the @"bin\patches" directory
            theStore.Schema.WritePatch(@"bin\patches".AppendPath("1.initial.sql"));
            // ENDSAMPLE

            fileSystem.FileExists(@"bin\patches".AppendPath("1.initial.sql"));
            fileSystem.FileExists(@"bin\patches".AppendPath("1.initial.drop.sql"));
        }

        [Fact]
        public void can_do_schema_validation_with_no_detected_changes()
        {
            theStore.Schema.EnsureStorageExists(typeof(User));
            theStore.Schema.EnsureStorageExists(typeof(Target));

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
        public void can_do_schema_validation_negative_case_with_detected_changes()
        {
            theStore.Schema.EnsureStorageExists(typeof(User));
            theStore.Schema.EnsureStorageExists(typeof(Target));

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>().Duplicate(x => x.UserName);
                _.Schema.For<Target>();
            }))
            {

                Exception<SchemaValidationException>.ShouldBeThrownBy(() =>
                {
                    store.Schema.AssertDatabaseMatchesConfiguration();
                }).Message.ShouldContain("user_name");

                
            }
        }

    }
}