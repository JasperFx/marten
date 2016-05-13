using Marten.Schema.Identity.Sequences;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity.Sequences
{
    public class IntHiloGeneratorTests
    {
        private readonly ISequence theSequence = Substitute.For<ISequence>();
        private IntHiloGenerator theGenerator;

        public IntHiloGeneratorTests()
        {
            theGenerator = new IntHiloGenerator(theSequence);
        }

        [Fact]
        public void do_nothing_when_we_have_a_valid_id()
        {
            bool assigned = false;

            theGenerator.Assign(5, out assigned).ShouldBe(5);

            assigned.ShouldBeFalse();
        }

        [Fact]
        public void assign_if_zero_id()
        {
            bool assigned = false;
            int next = 5;

            theSequence.NextInt().Returns(next);

            theGenerator.Assign(0, out assigned)
                .ShouldBe(next);

            assigned.ShouldBeTrue();
        }

        [Fact]
        public void assign_if_negative_id()
        {
            bool assigned = false;
            int next = 5;

            theSequence.NextInt().Returns(next);

            theGenerator.Assign(-1, out assigned)
                .ShouldBe(next);

            assigned.ShouldBeTrue();
        }
    }
}