using System.Linq;
using Marten.Schema;
using Marten.Schema.Sequences;
using Marten.Testing.Fixtures;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing.Schema.Sequences
{
    public class HiloIdGenerationTests
    {
        [Fact]
        public void create_argument_value()
        {
            var container = Container.For<DevelopmentModeRegistry>();

            var schema = container.GetInstance<IDocumentSchema>();

            var generation = new HiloIdGeneration(typeof(Target), new HiloSettings());

            generation.GetValue(schema).ShouldBeOfType<HiloSequence>()
                .EntityName.ShouldBe("Target");
        }

        [Fact]
        public void arguments_just_returns_itself()
        {
            var generation = new HiloIdGeneration(typeof(Target), new HiloSettings());
            generation.ToArguments().Single().ShouldBeSameAs(generation);
        }
    }
}