using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity
{
    public class IdentityKeyGenerationTests
    {
        [Fact]
        public void can_build_a_generator()
        {
            var settings = new HiloSettings();

            var sequence = Substitute.For<ISequence>();
            var sequences = Substitute.For<ISequences>();
            var schema = Substitute.For<IDocumentSchema>();

            schema.Sequences.Returns(sequences);
            sequences.Hilo(typeof(Target), settings).Returns(sequence);

            var mapping = DocumentMapping.For<Target>();
            var generation = new IdentityKeyGeneration(mapping, settings);


            var generator = generation.Build<string>(schema).ShouldBeOfType<IdentityKeyGenerator>();
            generator
                .Sequence.ShouldBeSameAs(sequence);

            generator.Alias.ShouldBe(mapping.Alias);
        }

        [Fact]
        public void assign_with_existing_id()
        {
            var sequence = Substitute.For<ISequence>();

            var generator = new IdentityKeyGenerator("foo", sequence);

            bool assigned = true;

            generator.Assign("foo/3", out assigned).ShouldBe("foo/3");

            assigned.ShouldBeFalse();

        }


        [Fact]
        public void assign_with_null_id()
        {
            var sequence = Substitute.For<ISequence>();
            sequence.NextLong().Returns(11);

            var generator = new IdentityKeyGenerator("foo", sequence);


            bool assigned = false;

            generator.Assign(null, out assigned).ShouldBe("foo/11");

            assigned.ShouldBeTrue();

        }


        [Fact]
        public void assign_with_empty_id()
        {
            var sequence = Substitute.For<ISequence>();
            sequence.NextLong().Returns(13);

            var generator = new IdentityKeyGenerator("foo", sequence);


            bool assigned = false;

            generator.Assign(string.Empty, out assigned).ShouldBe("foo/13");

            assigned.ShouldBeTrue();

        }
    }
}