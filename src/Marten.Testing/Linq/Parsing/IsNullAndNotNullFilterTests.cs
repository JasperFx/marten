using Marten.Linq.Fields;
using Marten.Linq.Filters;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq.Parsing
{
    public class IsNullAndNotNullFilterTests
    {
        [Fact]
        public void reverse_is_null()
        {
            var filter = new IsNullFilter(Substitute.For<IField>());
            filter.Reverse().ShouldBeOfType<IsNotNullFilter>()
                .Field.ShouldBe(filter.Field);
        }

        [Fact]
        public void reverse_not_null_is_null()
        {
            var filter = new IsNotNullFilter(Substitute.For<IField>());
            filter.Reverse().ShouldBeOfType<IsNullFilter>()
                .Field.ShouldBe(filter.Field);
        }
    }
}
