using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Linq
{
    public class using_containment_operator_in_linq_Tests : IntegrationContext
    {
        public using_containment_operator_in_linq_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
            DocumentTracking = DocumentTracking.IdentityOnly;
            StoreOptions(_ => { _.Schema.For<Target>().GinIndexJsonData(); });
        }

        [Fact]
        public void query_by_date()
        {
            var targets = Target.GenerateRandomData(6).ToArray();
            theSession.Store(targets);

            theSession.SaveChanges();

            var actual = theSession.Query<Target>().Where(x => x.Date == targets.ElementAt(2).Date)
                .ToArray();

            SpecificationExtensions.ShouldBeGreaterThan(actual.Length, 0);


            actual.ShouldContain(targets.ElementAt(2));
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
    }

    public class using_containment_operator_in_linq_with_camel_casing_Tests : IntegrationContext
    {
        public using_containment_operator_in_linq_with_camel_casing_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
            StoreOptions(_ =>
            {
                _.UseDefaultSerialization(EnumStorage.AsString, Casing.CamelCase);

                _.Schema.For<Target>().GinIndexJsonData();
            });
        }

        [Fact]
        public void query_by_date()
        {
            DocumentTracking = DocumentTracking.IdentityOnly;

            var targets = Target.GenerateRandomData(6).ToArray();
            theSession.Store(targets);

            theSession.SaveChanges();

            var actual = theSession.Query<Target>().Where(x => x.Date == targets.ElementAt(2).Date)
                .ToArray();

            SpecificationExtensions.ShouldBeGreaterThan(actual.Length, 0);


            actual.ShouldContain(targets.ElementAt(2));
        }

        [Fact]
        public void query_by_number()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.Store(new Target { Number = 5 });
            theSession.Store(new Target { Number = 6 });

            theSession.SaveChanges();


            theSession.Query<Target>().Where(x => x.Number == 3).Single().Number.ShouldBe(3);
        }

        [Fact]
        public void query_by_string()
        {
            theSession.Store(new Target { String = "Python" });
            theSession.Store(new Target { String = "Ruby" });
            theSession.Store(new Target { String = "Java" });
            theSession.Store(new Target { String = "C#" });
            theSession.Store(new Target { String = "Scala" });

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.String == "Python").Single().String.ShouldBe("Python");
        }
    }
}
