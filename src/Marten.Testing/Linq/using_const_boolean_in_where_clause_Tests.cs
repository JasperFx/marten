using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    [ControlledQueryStoryteller]
    public class using_const_boolean_in_where_clause_Tests : IntegrationContext
    {
        [Fact]
        public void where_const_false()
        {
            var target1 = new Target { Number = 1, String = "Foo" };
            var target2 = new Target { Number = 2, String = "Foo" };
            var target3 = new Target { Number = 1, String = "Bar" };
            var target4 = new Target { Number = 1, String = "Foo" };
            var target5 = new Target { Number = 2, String = "Bar" };
            theSession.Store(target1);
            theSession.Store(target2);
            theSession.Store(target3);
            theSession.Store(target4);
            theSession.Store(target5);
            theSession.SaveChanges();

            var q = Queryable.Where<Target>(theSession.Query<Target>(), x => false && x.Number == 1);
            q.Count().ShouldBe(0);
        }

        [Fact]
        public void where_const_true()
        {
            var target1 = new Target { Number = 1, String = "Foo" };
            var target2 = new Target { Number = 2, String = "Foo" };
            var target3 = new Target { Number = 1, String = "Bar" };
            var target4 = new Target { Number = 1, String = "Foo" };
            var target5 = new Target { Number = 2, String = "Bar" };
            theSession.Store(target1);
            theSession.Store(target2);
            theSession.Store(target3);
            theSession.Store(target4);
            theSession.Store(target5);
            theSession.SaveChanges();

            var q = Queryable.Where<Target>(theSession.Query<Target>(), x => true && x.Number == 1);
            q.Count().ShouldBe(3);
        }

        public using_const_boolean_in_where_clause_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
