using Marten.Schema;
using Marten.Schema.Sequences;
using Marten.Testing.Fixtures;
using Shouldly;
using StructureMap;

namespace Marten.Testing.Schema.Sequences
{
    public class SequenceFactoryTests
    {
        private readonly IContainer _container = Container.For<DevelopmentModeRegistry>();

        public SequenceFactoryTests()
        {
            _container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();
        }

        public void can_create_table_on_fly_if_necessary()
        {
            var factory = _container.GetInstance<SequenceFactory>();

            factory.HiLo(typeof (Target), new HiloDef())
                .ShouldBeOfType<HiLoSequence>();

            var schema = _container.GetInstance<IDocumentSchema>();
            schema.SchemaTableNames()
                .ShouldContain("mt_hilo");

            schema.SchemaFunctionNames().ShouldContain("mt_get_next_hi");
        }
    }
}