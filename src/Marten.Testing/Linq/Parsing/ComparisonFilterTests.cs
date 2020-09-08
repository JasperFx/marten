using Marten.Linq.Filters;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq.Parsing
{
    public class ComparisonFilterTests
    {
        [Fact]
        public void reverse()
        {
            var where = new ComparisonFilter(null, null, "=");
            var reversed = where.Reverse();

            // I know, FP guys are going to go nuts, but it's
            // not shared over threads and this is less allocations
            // than a full clone
            reversed.ShouldBeSameAs(where);

            where.Op.ShouldBe("!=");
        }
    }
}
