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
            var schema = container.GetInstance<IDocumentSchema>();

            build_and_verify_mt_stream(container, schema);
        }

        private static void build_and_verify_mt_stream(IContainer container, IDocumentSchema schema)
        {
            container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();

            var runner = container.GetInstance<IManagedConnection>();
            var sql = SchemaBuilder.GetSqlScript(schema.StoreOptions, "mt_stream");

            runner.Execute(sql);

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
            var container = Container.For<DevelopmentModeRegistry>();
            var schema = container.GetInstance<IDocumentSchema>();
            schema.StoreOptions.DatabaseSchemaName = "other";

            container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();

            var runner = container.GetInstance<IManagedConnection>();
            var sql = SchemaBuilder.GetSqlScript(schema.StoreOptions, "mt_stream");

            runner.Execute(sql);

            var schemaFunctionNames = schema.SchemaFunctionNames();
            schemaFunctionNames.ShouldContain("other.mt_append_event");
            schemaFunctionNames.ShouldContain("other.mt_version_stream");

            var schemaTableNames = schema.SchemaTableNames();
            schemaTableNames.ShouldContain("other.mt_streams");
            schemaTableNames.ShouldContain("other.mt_events");
        }
    }
}