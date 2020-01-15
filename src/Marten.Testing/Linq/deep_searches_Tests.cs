using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Linq
{
    [ControlledQueryStoryteller]
    public class deep_searches_Tests: DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void query_two_deep()
        {
            theSession.Store(new Target { Inner = new Target { Number = 1, String = "Jeremy" } });
            theSession.Store(new Target { Inner = new Target { Number = 2, String = "Max" } });
            theSession.Store(new Target { Inner = new Target { Number = 1, String = "Declan" } });
            theSession.Store(new Target { Inner = new Target { Number = 2, String = "Lindsey" } });
            theSession.Store(new Target { String = "Russell" });

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Inner.Number == 2).ToArray().OrderBy(x => x.Inner.String)
                .Select(x => x.Inner.String)
                .ShouldHaveTheSameElementsAs("Lindsey", "Max");
        }

        [Fact]
        public void query_three_deep()
        {
            theSession.Store(new Target { Number = 1, Inner = new Target { Inner = new Target { Long = 1 } } });
            theSession.Store(new Target { Number = 2, Inner = new Target { Inner = new Target { Long = 2 } } });
            theSession.Store(new Target { Number = 3, Inner = new Target { Inner = new Target { Long = 1 } } });
            theSession.Store(new Target { Number = 4, Inner = new Target { Inner = new Target { Long = 2 } } });

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Inner.Inner.Long == 1).ToArray().Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1, 3);
        }

        [Fact]
        public void order_by_2_deep()
        {
            theSession.Store(new Target { Inner = new Target { Number = 1, String = "Jeremy" } });
            theSession.Store(new Target { Inner = new Target { Number = 2, String = "Max" } });
            theSession.Store(new Target { Inner = new Target { Number = 1, String = "Declan" } });
            theSession.Store(new Target { Inner = new Target { Number = 2, String = "Lindsey" } });
            theSession.Store(new Target { String = "Russell" });

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Inner.Number == 2).OrderBy(x => x.Inner.String).ToArray()
                .Select(x => x.Inner.String)
                .ShouldHaveTheSameElementsAs("Lindsey", "Max");
        }

        [Fact]
        public void query_two_deep_with_containment_operator()
        {
            theStore.Tenancy.Default.MappingFor(typeof(Target)).As<DocumentMapping>().PropertySearching = PropertySearching.ContainmentOperator;

            theSession.Store(new Target { Inner = new Target { Number = 1, String = "Jeremy" } });
            theSession.Store(new Target { Inner = new Target { Number = 2, String = "Max" } });
            theSession.Store(new Target { Inner = new Target { Number = 1, String = "Declan" } });
            theSession.Store(new Target { Inner = new Target { Number = 2, String = "Lindsey" } });
            theSession.Store(new Target { String = "Russell" });

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Inner.Number == 2).ToArray().OrderBy(x => x.Inner.String)
                .Select(x => x.Inner.String)
                .ShouldHaveTheSameElementsAs("Lindsey", "Max");
        }

        [Fact]
        public void query_three_deep_with_containment_operator()
        {
            theStore.Tenancy.Default.MappingFor(typeof(Target)).As<DocumentMapping>().PropertySearching = PropertySearching.ContainmentOperator;

            theSession.Store(new Target { Number = 1, Inner = new Target { Inner = new Target { Long = 1 } } });
            theSession.Store(new Target { Number = 2, Inner = new Target { Inner = new Target { Long = 2 } } });
            theSession.Store(new Target { Number = 3, Inner = new Target { Inner = new Target { Long = 1 } } });
            theSession.Store(new Target { Number = 4, Inner = new Target { Inner = new Target { Long = 2 } } });

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Inner.Inner.Long == 1).ToArray().Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1, 3);
        }

        [Fact]
        public void order_by_2_deep_with_containment_operator()
        {
            theStore.Tenancy.Default.MappingFor(typeof(Target)).As<DocumentMapping>().PropertySearching = PropertySearching.ContainmentOperator;

            theSession.Store(new Target { Inner = new Target { Number = 1, String = "Jeremy" } });
            theSession.Store(new Target { Inner = new Target { Number = 2, String = "Max" } });
            theSession.Store(new Target { Inner = new Target { Number = 1, String = "Declan" } });
            theSession.Store(new Target { Inner = new Target { Number = 2, String = "Lindsey" } });
            theSession.Store(new Target { String = "Russell" });

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Inner.Number == 2).OrderBy(x => x.Inner.String).ToArray()
                .Select(x => x.Inner.String)
                .ShouldHaveTheSameElementsAs("Lindsey", "Max");
        }
    }
}
