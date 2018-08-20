using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class invoking_queryable_count_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        
        [Fact]
        public void count_without_any_where()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.SaveChanges();

            theSession.Query<Target>().Count().ShouldBe(4);
        }

        [Fact]
        public void long_count_without_any_where()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.SaveChanges();

            theSession.Query<Target>().LongCount().ShouldBe(4);
        }

	    [Fact]
	    public void count_matching_properties_within_type()
	    {			
		    var t1= new Target();
		    t1.OtherGuid = t1.Id;
		    var t2 = new Target();
		    t2.OtherGuid = t2.Id;

		    theSession.Store(t1);
		    theSession.Store(t2);
			theSession.Store(new Target());
		    theSession.Store(new Target());		    
		    theSession.SaveChanges();
			theSession.Query<Target>().Count(x => x.Id == x.OtherGuid).ShouldBe(2);
		}

	    [Fact]
	    public void count_matching_properties_within_type_notequals()
	    {		    
		    var t1 = new Target();
		    t1.OtherGuid = t1.Id;
		    var t2 = new Target();
		    t2.OtherGuid = t2.Id;

		    theSession.Store(t1);
		    theSession.Store(t2);
		    theSession.Store(new Target());
		    theSession.Store(new Target());
		    theSession.SaveChanges();
		    theSession.Query<Target>().Count(x => x.Id != x.OtherGuid).ShouldBe(2);
	    }

		// Well, this is pretty much a redundant test (since we're testing the Linq translation) but covers #1067
		[Fact]
	    public async Task count_matching_properties_within_type_async()
	    {		    
		    var t1 = new Target();
		    t1.OtherGuid = t1.Id;
		    var t2 = new Target();
		    t2.OtherGuid = t2.Id;

		    theSession.Store(t1);
		    theSession.Store(t2);
		    theSession.Store(new Target());
		    theSession.Store(new Target());
		    theSession.SaveChanges();
		    var count = await theSession.Query<Target>().CountAsync(x => x.Id == x.OtherGuid);
			count.ShouldBe(2);
		}


		[Fact]
        public void long_count_with_a_where_clause()
        {
            // theSession is an IDocumentSession in this test
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.Store(new Target { Number = 5 });
            theSession.Store(new Target { Number = 6 });
            theSession.SaveChanges();

            theSession.Query<Target>().LongCount(x => x.Number > 3).ShouldBe(3);
        }

        [Fact]
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