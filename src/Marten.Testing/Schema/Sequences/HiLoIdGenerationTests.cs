using System.Linq;
using Marten.Schema;
using Marten.Schema.Sequences;
using Marten.Testing.Fixtures;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing.Schema.Sequences
{
    public class HiLoIdGenerationTests
    {
        [Fact]
        public void create_argument_value()
        {
            var container = Container.For<DevelopmentModeRegistry>();

            var schema = container.GetInstance<IDocumentSchema>();

            var generation = new HiloIdGeneration(typeof(Target), new HiloDef());

            generation.GetValue(schema).ShouldBeOfType<HiLoSequence>()
                .EntityName.ShouldBe("Target");
 
        }

        [Fact]
        public void arguments_just_returns_itself()
        {
            var generation = new HiloIdGeneration(typeof(Target), new HiloDef());
            generation.ToArguments().Single().ShouldBeSameAs(generation);
        }




        
    }
}