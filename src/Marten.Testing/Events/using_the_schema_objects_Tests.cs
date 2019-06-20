using System;
using Marten.Events;
using Marten.Testing.Events.Projections;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class using_the_schema_objects_Tests
    {
        [Fact]
        public void can_build_schema_with_auto_create_none()
        {
            var id = Guid.NewGuid();

            using (var store1 = DocumentStore.For(ConnectionSource.ConnectionString))
            {
                using (var session = store1.OpenSession())
                {
                    session.Events.StartStream<Quest>(id, new QuestStarted { Name = "Destroy the Orb" },
                        new MonsterSlayed { Name = "Troll" }, new MonsterSlayed { Name = "Dragon" });
                    session.SaveChanges();
                }
            }

            // SAMPLE: registering-event-types
            var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.None;

                _.Events.AddEventType(typeof(QuestStarted));
                _.Events.AddEventType(typeof(MonsterSlayed));
            });
            // ENDSAMPLE

            using (var session = store2.OpenSession())
            {
                session.Events.FetchStream(id).Count.ShouldBe(3);
            }

            store2.Dispose();
        }

        [Fact]
        public void can_build_the_event_schema_objects_in_a_separted_schema()
        {
            var store = TestingDocumentStore.For(_ =>
            {
                // SAMPLE: override_schema_name_event_store
                _.Events.DatabaseSchemaName = "event_store";
                // ENDSAMPLE
            });

            store.Tenancy.Default.EnsureStorageExists(typeof(EventStream));

            var schemaDbObjectNames = store.Tenancy.Default.DbObjects.Functions();
            schemaDbObjectNames.ShouldContain("event_store.mt_append_event");

            var schemaTableNames = store.Tenancy.Default.DbObjects.SchemaTables();
            schemaTableNames.ShouldContain("event_store.mt_streams");
            schemaTableNames.ShouldContain("event_store.mt_events");
        }

        [Fact]
        public void can_build_the_mt_stream_schema_objects()
        {
            var store = TestingDocumentStore.Basic();

            store.Tenancy.Default.EnsureStorageExists(typeof(EventStream));

            var schemaDbObjectNames = store.Tenancy.Default.DbObjects.Functions();
            schemaDbObjectNames.ShouldContain("public.mt_append_event");

            var schemaTableNames = store.Tenancy.Default.DbObjects.SchemaTables();
            schemaTableNames.ShouldContain("public.mt_streams");
            schemaTableNames.ShouldContain("public.mt_events");
            schemaTableNames.ShouldContain("public.mt_event_progression");
        }

        [Fact]
        public void can_build_the_mt_stream_schema_objects_in_different_database_schema()
        {
            var store = TestingDocumentStore.For(_ =>
            {
                _.Events.DatabaseSchemaName = "other";
            });

            store.Tenancy.Default.EnsureStorageExists(typeof(EventStream));

            var schemaDbObjectNames = store.Tenancy.Default.DbObjects.Functions();
            schemaDbObjectNames.ShouldContain("other.mt_append_event");

            var schemaTableNames = store.Tenancy.Default.DbObjects.SchemaTables();
            schemaTableNames.ShouldContain("other.mt_streams");
            schemaTableNames.ShouldContain("other.mt_events");
            schemaTableNames.ShouldContain("other.mt_event_progression");
        }
    }
}
