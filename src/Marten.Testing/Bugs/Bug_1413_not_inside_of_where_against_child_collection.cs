using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Bugs
{
    public class Bug_1413_not_inside_of_where_against_child_collection : IntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public Bug_1413_not_inside_of_where_against_child_collection(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            _output = output;
        }

        [Fact]
        public void can_do_so()
        {
            var results = theSession.Query<Target>().Where(x => x.Children.Any(c => c.String == "hello" && c.Color != Colors.Blue))
                .ToList();

            results.ShouldNotBeNull();
        }
    }
}
