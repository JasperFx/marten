using LamarCodeGeneration.Util;
using Marten.Linq.Filters;
using NSubstitute;
using Shouldly;
using Weasel.Postgresql.SqlGeneration;
using Xunit;
using NotWhereFragment = Weasel.Postgresql.SqlGeneration.NotWhereFragment;

namespace Marten.Testing.Linq.Parsing
{
    public class NotWhereFragmentTests
    {
        [Fact]
        public void register_against_an_inner_fragment_that_is_not_reversiable()
        {
            var parent = Substitute.For<IWhereFragmentHolder>();
            var not = new NotWhereFragment(parent);

            var where = new WhereFragment("a = b");

            not.As<IWhereFragmentHolder>().Register(where);

            not.Inner.ShouldBe(where);

            parent.Received().Register(not);
        }

        [Fact]
        public void register_against_an_inner_fragment_that_is_reversible()
        {
            var parent = Substitute.For<IWhereFragmentHolder>();
            var not = new NotWhereFragment(parent);

            var reversible = Substitute.For<IReversibleWhereFragment>();
            var reversed = new WhereFragment("a = b");
            reversible.Reverse().Returns(reversed);

            not.As<IWhereFragmentHolder>().Register(reversible);

            parent.Received().Register(reversed);
        }
    }
}
