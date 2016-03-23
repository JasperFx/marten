using Marten.Schema;
using Marten.Schema.Sequences;
using Marten.Testing.Fixtures;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing.Schema.Sequences
{
    public class SequenceFactoryTests : IntegratedFixture
    {
        private readonly IContainer _container = Container.For<DevelopmentModeRegistry>();

        public SequenceFactoryTests()
        {
            _container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();
        }

        [Fact]
        public void can_create_table_on_fly_if_necessary()
        {
            var factory = _container.GetInstance<SequenceFactory>();

            factory.Hilo(typeof (Target), new HiloSettings())
                .ShouldBeOfType<HiloSequence>();

            var schema = _container.GetInstance<IDocumentSchema>();
            schema.SchemaTableNames()
                .ShouldContain("public.mt_hilo");

            schema.SchemaFunctionNames().ShouldContain("public.mt_get_next_hi");
        }
    }
}