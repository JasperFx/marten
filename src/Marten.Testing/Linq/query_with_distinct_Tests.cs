using System.Linq;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class query_with_distinct_Tests : DocumentSessionFixture<NulloIdentityMap> {

        [Fact]
        public void get_distinct_string() {
            theSession.Store(new Target { String = "one"});
            theSession.Store(new Target { String = "one" });
            theSession.Store(new Target { String = "two" });
            theSession.Store(new Target { String = "two" });
            theSession.Store(new Target { String = "three" });
            theSession.Store(new Target { String = "three" });

            theSession.SaveChanges();

            var queryable = theSession.Query<Target>().Select(x => x.String).Distinct();

            queryable.ToList().Count.ShouldBe(3);

        }

        [Fact]
        public void get_distinct_strings() {
            theSession.Store(new Target { String = "one", AnotherString = "one"});
            theSession.Store(new Target { String = "one", AnotherString = "two" });
            theSession.Store(new Target { String = "one", AnotherString = "two" });
            theSession.Store(new Target { String = "two", AnotherString = "one" });
            theSession.Store(new Target { String = "two", AnotherString = "two" });
            theSession.Store(new Target { String = "two", AnotherString = "two" });

            theSession.SaveChanges();

            var queryable = theSession.Query<Target>().Select(x => new {
                x.String, x.AnotherString}).Distinct();

            queryable.ToList().Count.ShouldBe(4);

        }

    }
}
