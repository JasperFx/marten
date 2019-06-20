using System.Linq;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_503_query_on_null_complex_object: IntegratedFixture
    {
        [Fact]
        public void should_not_blow_up_when_querying_for_null_object()
        {
            using (var sessionOne = theStore.OpenSession())
            {
                sessionOne.Store(new Target { String = "Something", Inner = new Target(), AnotherString = "first" });
                sessionOne.Store(new Target { String = "Something", Inner = null, AnotherString = "second" });

                sessionOne.SaveChanges();
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
    }
}
