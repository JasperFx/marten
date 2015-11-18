using Marten.Schema;
using Shouldly;
using StructureMap;

namespace Marten.Testing.Events
{
    public class using_the_schema_objects_Tests
    {
        public void can_build_the_mt_stream_schema_objects()
        {
            var container = Container.For<DevelopmentModeRegistry>();
            container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();

            var runner = container.GetInstance<CommandRunner>();
            var sql = SchemaBuilder.GetText("mt_stream");

            runner.Execute(sql);

            var schema = container.GetInstance<IDocumentSchema>();

            schema.SchemaFunctionNames().ShouldContain("mt_append_event");
            schema.SchemaFunctionNames().ShouldContain("mt_version_stream");

            schema.SchemaTableNames().ShouldContain("mt_streams");
            schema.SchemaTableNames().ShouldContain("mt_events");
        } 
    }
}