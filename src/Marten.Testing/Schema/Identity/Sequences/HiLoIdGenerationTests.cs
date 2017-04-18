using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Marten.Storage;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity.Sequences
{
    public class HiloIdGenerationTests
    {
        [Fact]
        public void build_assignment_for_int()
        {
            var settings = new HiloSettings();

            var sequence = Substitute.For<ISequence>();
            var sequences = Substitute.For<ISequences>();
            var tenant = Substitute.For<ITenant>();

            tenant.Sequences.Returns(sequences);
            sequences.Hilo(typeof(Target), settings).Returns(sequence);

            var generation = new HiloIdGeneration(typeof(Target), settings);


            generation.Build<int>(tenant).ShouldBeOfType<IntHiloGenerator>()
                .Sequence.ShouldBeSameAs(sequence);
        }

        [Fact]
        public void build_assignment_for_long()
        {
            var settings = new HiloSettings();

            var sequence = Substitute.For<ISequence>();
            var sequences = Substitute.For<ISequences>();
            var tenant = Substitute.For<ITenant>();

            tenant.Sequences.Returns(sequences);
            sequences.Hilo(typeof(Target), settings).Returns(sequence);

            var generation = new HiloIdGeneration(typeof(Target), settings);


            generation.Build<long>(tenant).ShouldBeOfType<LongHiloGenerator>()
                .Sequence.ShouldBeSameAs(sequence);
        }

        [Fact]
        public void key_types()
        {
            var generation = new HiloIdGeneration(typeof(Target), new HiloSettings());
            generation.KeyTypes.ShouldHaveTheSameElementsAs(typeof(int), typeof(long));
        }
    }
}