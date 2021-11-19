using System;
using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1454_comparison_between_data_offset_in_child_collection : IntegrationContext
    {
        public Bug_1454_comparison_between_data_offset_in_child_collection(DefaultStoreFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public void can_query_against_DateTime()
        {
            theSession.Query<Target>().SelectMany(x => x.Children)
                .Where(x => x.Date == DateTime.Today.AddDays(-3))
                .ToList().ShouldNotBeNull();
        }

        [Fact]
        public void can_query_against_DateTimeOffset()
        {
            theSession.Query<Target>().SelectMany(x => x.Children)
                .Where(x => x.DateOffset == DateTimeOffset.UtcNow)
                .ToList().ShouldNotBeNull();
        }
    }
}
