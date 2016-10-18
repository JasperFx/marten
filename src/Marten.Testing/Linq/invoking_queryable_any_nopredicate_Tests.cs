using System.Linq;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class invoking_queryable_any_nopredicate_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        
        [Fact]
        public void naked_any_hit()
        {
            var targetithchildren = new Target {Number = 1};
            targetithchildren.Children = new[] {new Target(),};
            var nochildrennullarray = new Target { Number = 2 };
            var nochildrenemptyarray = new Target { Number = 3 };
            nochildrenemptyarray.Children = new Target[] {};
            theSession.Store(nochildrennullarray);
            theSession.Store(nochildrenemptyarray);
            theSession.Store(targetithchildren);
            theSession.SaveChanges();

            var items = theSession.Query<Target>().Where(x => x.Children.Any()).ToList();

            items.Count.ShouldBe(1);
        }
                                     
    }
}