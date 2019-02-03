using System;
using System.Linq;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Linq
{
    [ControlledQueryStoryteller]
    public class query_with_dates_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void can_select_DateTimeOffset_and_will_return_localtime()
        {
            var document = Target.Random();
            document.DateOffset = DateTimeOffset.UtcNow;

            using (var session = theStore.OpenSession())
            {
                session.Insert(document);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                var dateOffset = query.Query<Target>().Select(x => x.DateOffset).Single();

                // be aware of the Npgsql DateTime mapping https://www.npgsql.org/doc/types/datetime.html
                dateOffset.ShouldBeEqualWithDbPrecision(document.DateOffset.ToLocalTime());
            }
        }
    }
}