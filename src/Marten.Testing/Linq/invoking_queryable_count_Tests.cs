using System.Linq;
using Marten.Services;
using Marten.Testing.Fixtures;
using Shouldly;

namespace Marten.Testing.Linq
{
    public class invoking_queryable_count_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        
        public void count_without_any_where()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.SaveChanges();

            theSession.Query<Target>().Count().ShouldBe(4);
        }

        // SAMPLE: using_count
        public void count_with_a_where_clause()
        {
            // theSession is an IDocumentSession in this test
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.Store(new Target { Number = 5 });
            theSession.Store(new Target { Number = 6 });
            theSession.SaveChanges();

            theSession.Query<Target>().Count(x => x.Number > 3).ShouldBe(3);
        }
        // ENDSAMPLE
    }
}