using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Fixtures;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class delete_many_documents_by_query_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void can_delete_by_query()
        {
            var targets = Target.GenerateRandomData(50).ToArray();
            for (var i = 0; i < 15; i++)
            {
                targets[i].Double = 578;
            }

            theStore.BulkInsert(targets);

            var initialCount = theSession.Query<Target>().Where(x => x.Double == 578).Count();

            theSession.DeleteWhere<Target>(x => x.Double == 578);
            theSession.SaveChanges();

            theSession.Query<Target>().Count().ShouldBe(50 - initialCount);

            theSession.Query<Target>().Where(x => x.Double == 578).Count()
                .ShouldBe(0);

        }

        [Fact]
        public void in_a_mix_with_other_commands()
        {
            var targets = Target.GenerateRandomData(50).ToArray();
            for (var i = 0; i < 15; i++)
            {
                targets[i].Double = 578;
            }

            theStore.BulkInsert(targets);

            var initialCount = theSession.Query<Target>().Where(x => x.Double == 578).Count();

            theSession.Store(new User(), new User(), new User());
            theSession.DeleteWhere<Target>(x => x.Double == 578);
            theSession.SaveChanges();

            theSession.Query<Target>().Count().ShouldBe(50 - initialCount);

            theSession.Query<Target>().Where(x => x.Double == 578).Count()
                .ShouldBe(0);

            theSession.Query<User>().Count().ShouldBe(3);
        }
    }
}