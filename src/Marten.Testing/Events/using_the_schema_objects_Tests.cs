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
            container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();

            var runner = container.GetInstance<ICommandRunner>();
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