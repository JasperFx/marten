using System.Linq;
using Baseline;
using Marten.Services;
using Marten.Testing.Fixtures;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class using_containment_operator_in_linq_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        public using_containment_operator_in_linq_Tests()
        {
            theStore.Schema.Alter(_ =>
            {
                _.For<Target>().GinIndexJsonData();
            });
        }

        [Fact]
        public void query_by_string()
        {
            theSession.Store(new Target {String = "Python"});
            theSession.Store(new Target {String = "Ruby"});
            theSession.Store(new Target {String = "Java"});
            theSession.Store(new Target {String = "C#"});
            theSession.Store(new Target {String = "Scala"});

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.String == "Python").Single().String.ShouldBe("Python");
        }

        [Fact]
        public void query_by_number()
        {
            theSession.Store(new Target {Number = 1});
            theSession.Store(new Target {Number = 2});
            theSession.Store(new Target {Number = 3});
            theSession.Store(new Target {Number = 4});
            theSession.Store(new Target {Number = 5});
            theSession.Store(new Target {Number = 6});

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Number == 3).Single().Number.ShouldBe(3);
        }

        [Fact]
        public void query_by_date()
        {
            var targets = Target.GenerateRandomData(6).ToArray();
            targets.Each(x => theSession.Store(x));

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Date == targets.ElementAt(2).Date)
                .ToArray().ShouldContain(x => x.Date == targets.ElementAt(2).Date);
        }
    }
}