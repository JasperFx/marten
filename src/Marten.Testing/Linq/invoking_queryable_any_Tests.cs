using System.Linq;
using Marten.Testing.Fixtures;

namespace Marten.Testing.Linq
{
    public class invoking_queryable_any_Tests : DocumentSessionFixture
    {

        public void any_miss_with_query()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.SaveChanges();

            theSession.Query<Target>().Any(x => x.Number == 11)
                .ShouldBeFalse();
                
        }


        public void naked_any_miss()
        {
            theSession.Query<Target>().Any()
                .ShouldBeFalse();

        }

        public void naked_any_hit()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.SaveChanges();

            theSession.Query<Target>().Any().ShouldBeTrue();
        }


        public void any_hit_with_only_one_document()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.SaveChanges();

            theSession.Query<Target>().Any(x => x.Number == 3)
                .ShouldBeTrue();
        }


        public void any_hit_with_more_than_one_match()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 4 });
            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Number == 2).Any()
                .ShouldBeTrue();
        }
    }
}