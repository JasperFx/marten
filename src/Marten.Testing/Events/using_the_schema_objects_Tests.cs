using System.Diagnostics;
using Baseline;
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
    }
}