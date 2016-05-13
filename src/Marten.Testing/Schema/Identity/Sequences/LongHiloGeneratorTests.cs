using Marten.Schema.Identity.Sequences;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity.Sequences
{
    public class LongHiloGeneratorTests
    {
        private readonly ISequence theSequence = Substitute.For<ISequence>();
        private LongHiloGenerator theGenerator;

        public LongHiloGeneratorTests()
        {
            theGenerator = new LongHiloGenerator(theSequence);
        }

        [Fact]
        public void do_nothing_when_we_have_a_valid_id()
        {
            bool assigned = false;

            theGenerator.Assign(5L, out assigned).ShouldBe(5);

            assigned.ShouldBeFalse();
        }

        [Fact]
        public void assign_if_zero_id()
        {
            bool assigned = false;
            long next = 5;

            theSequence.NextLong().Returns(next);

            theGenerator.Assign(0, out assigned)
                .ShouldBe(next);

            assigned.ShouldBeTrue();
        }

        [Fact]
        public void assign_if_negative_id()
        {
            bool assigned = false;
            int next = 5;

            theSequence.NextLong().Returns(next);

            theGenerator.Assign(-1, out assigned)
                .ShouldBe(next);

            assigned.ShouldBeTrue();
        }
    }
}