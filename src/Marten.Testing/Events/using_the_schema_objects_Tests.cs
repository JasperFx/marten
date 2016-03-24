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

            var schemaTableNames = schema.SchemaTableNames();
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

            var schemaTableNames = schema.SchemaTableNames();
            schemaTableNames.ShouldContain("other.mt_streams");
            schemaTableNames.ShouldContain("other.mt_events");
        }

        [Fact]
        public void can_build_the_event_schema_objects_in_a_separted_schema()
        {
            var container = ContainerFactory.Configure(options => options.Events.DatabaseSchemaName = "event_store");
            container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();

            var schema = container.GetInstance<IDocumentSchema>();
            schema.EnsureStorageExists(typeof(EventStream));

            var store = container.GetInstance<IDocumentStore>();
            store.EventStore.InitializeEventStoreInDatabase(true);

            var schemaFunctionNames = schema.SchemaFunctionNames();
            schemaFunctionNames.ShouldContain("event_store.mt_append_event");
            schemaFunctionNames.ShouldContain("event_store.mt_version_stream");

            var schemaTableNames = schema.SchemaTableNames();
            schemaTableNames.ShouldContain("event_store.mt_streams");
            schemaTableNames.ShouldContain("event_store.mt_events");
        }
    }
}