using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
namespace LinqTests.Bugs;

public class Bug_503_query_on_null_complex_object: IntegrationContext
{
    [Fact]
    public async Task should_not_blow_up_when_querying_for_null_object()
    {
        using (var sessionOne = theStore.LightweightSession())
        {
            sessionOne.Store(new Target { String = "Something", Inner = new Target(), AnotherString = "first" });
            sessionOne.Store(new Target { String = "Something", Inner = null, AnotherString = "second" });

            await sessionOne.SaveChangesAsync();
        }

        using (var querySession = theStore.QuerySession())
        {
            var targets = querySession.Query<Target>()
                .Where(x => x.String == "Something" && x.Inner != null)
                .ToList();

            targets.Count.ShouldBe(1);
            targets.First().AnotherString.ShouldBe("first");
        }
    }

    public Bug_503_query_on_null_complex_object(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
