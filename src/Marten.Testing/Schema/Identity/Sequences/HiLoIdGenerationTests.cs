using System.Linq;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Marten.Testing.Fixtures;
using NSubstitute;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing.Schema.Identity.Sequences
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

        [Fact]
        public void key_types()
        {
            var generation = new HiloIdGeneration(typeof(Target), new HiloSettings());
            generation.KeyTypes.ShouldHaveTheSameElementsAs(typeof(int), typeof(long));
        }

        [Fact]
        public void build_assignment_for_int()
        {
            var settings = new HiloSettings();

            var sequence = Substitute.For<ISequence>();
            var sequences = Substitute.For<ISequences>();
            var schema = Substitute.For<IDocumentSchema>();

            schema.Sequences.Returns(sequences);
            sequences.Hilo(typeof(Target), settings).Returns(sequence);

            var generation = new HiloIdGeneration(typeof(Target), settings);


            generation.Build<int>(schema).ShouldBeOfType<IntHiloGenerator>()
                .Sequence.ShouldBeSameAs(sequence);
        }

        [Fact]
        public void build_assignment_for_long()
        {
            var settings = new HiloSettings();

            var sequence = Substitute.For<ISequence>();
            var sequences = Substitute.For<ISequences>();
            var schema = Substitute.For<IDocumentSchema>();

            schema.Sequences.Returns(sequences);
            sequences.Hilo(typeof(Target), settings).Returns(sequence);

            var generation = new HiloIdGeneration(typeof(Target), settings);


            generation.Build<long>(schema).ShouldBeOfType<LongHiloGenerator>()
                .Sequence.ShouldBeSameAs(sequence);
        }
    }
}