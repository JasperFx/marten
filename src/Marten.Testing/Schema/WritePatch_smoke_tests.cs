using System;
using Baseline;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Events;
using Marten.Testing.Fixtures;
using Xunit;

namespace Marten.Testing.Schema
{
    public class WritePatch_smoke_tests : IntegratedFixture
    {
        [Fact]
        public void writes_both_the_update_and_rollback_files()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>();
                _.Events.AddEventType(typeof(MembersJoined));
            });

            var fileSystem = new FileSystem();
            fileSystem.DeleteDirectory("patches");
            fileSystem.CreateDirectory("patches");

            theStore.Schema.WritePatch("patches".AppendPath("1.initial.sql"));

            fileSystem.FileExists("patches".AppendPath("1.initial.sql"));
            fileSystem.FileExists("patches".AppendPath("1.initial.drop.sql"));
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