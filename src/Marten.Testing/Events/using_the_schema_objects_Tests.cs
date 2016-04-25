using Marten.Events;
using Marten.Schema;
using Marten.Services;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing.Events
{
    public class using_the_schema_objects_Tests
    {
        [Fact]
        public void can_build_the_mt_stream_schema_objects()
        {
            var container = Container.For<DevelopmentModeRegistry>();
            container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();

            var schema = container.GetInstance<IDocumentSchema>();
            schema.EnsureStorageExists(typeof(EventStream));

            var store = container.GetInstance<IDocumentStore>();
            store.EventStore.InitializeEventStoreInDatabase(true);

            var schemaFunctionNames = schema.SchemaFunctionNames();
            schemaFunctionNames.ShouldContain("public.mt_append_event");
            schemaFunctionNames.ShouldContain("public.mt_version_stream");

            var schemaTableNames = schema.SchemaTables();
            schemaTableNames.ShouldContain("public.mt_streams");
            schemaTableNames.ShouldContain("public.mt_events");
        }

        [Fact]
        public void can_build_the_mt_stream_schema_objects_in_different_database_schema()
        {
            var container = ContainerFactory.Configure(options => options.DatabaseSchemaName = "other");
            container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();

            var schema = container.GetInstance<IDocumentSchema>();
            schema.EnsureStorageExists(typeof(EventStream));

            var store = container.GetInstance<IDocumentStore>();
            store.EventStore.InitializeEventStoreInDatabase(true);

            var schemaFunctionNames = schema.SchemaFunctionNames();
            schemaFunctionNames.ShouldContain("other.mt_append_event");
            schemaFunctionNames.ShouldContain("other.mt_version_stream");

            var schemaTableNames = schema.SchemaTables();
            schemaTableNames.ShouldContain("other.mt_streams");
            schemaTableNames.ShouldContain("other.mt_events");
        }

        [Fact]
        public void can_build_the_event_schema_objects_in_a_separted_schema()
        {
            var container = ContainerFactory.Configure(_ =>
                // SAMPLE: override_schema_name_event_store
                _.Events.DatabaseSchemaName = "event_store"
                // ENDSAMPLE
            );
            container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();

            var schema = container.GetInstance<IDocumentSchema>();
            schema.EnsureStorageExists(typeof(EventStream));

            var store = container.GetInstance<IDocumentStore>();
            store.EventStore.InitializeEventStoreInDatabase(true);

            var schemaFunctionNames = schema.SchemaFunctionNames();
            schemaFunctionNames.ShouldContain("event_store.mt_append_event");
            schemaFunctionNames.ShouldContain("event_store.mt_version_stream");

            var schemaTableNames = schema.SchemaTables();
            schemaTableNames.ShouldContain("event_store.mt_streams");
            schemaTableNames.ShouldContain("event_store.mt_events");
        }

        [Fact]
        public void can_build_schema_with_auto_create_none()
        {
            var container1 = ContainerFactory.Configure(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
            });

            var schema1 = container1.GetInstance<IDocumentSchema>();
            var eventStorage1 = schema1.StorageFor(typeof (EventStream));

            eventStorage1.ShouldNotBeNull();

            var container2 = ContainerFactory.Configure(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.None;
            });

            var schema2 = container2.GetInstance<IDocumentSchema>();
            var eventStorage2 = schema2.StorageFor(typeof (EventStream));

            eventStorage2.ShouldNotBeNull();
        }
    }
}