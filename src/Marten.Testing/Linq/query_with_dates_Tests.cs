using System;
using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Linq
{
    [ControlledQueryStoryteller]
    public class query_with_dates_Tests: IntegrationContextWithIdentityMap<NulloIdentityMap>
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
                var dateOffset = query.Query<Target>().Where(x => x.Id == document.Id).Select(x => x.DateOffset).Single();

                // be aware of the Npgsql DateTime mapping https://www.npgsql.org/doc/types/datetime.html
                dateOffset.ShouldBeEqualWithDbPrecision(document.DateOffset.ToLocalTime());
            }
        }

        public query_with_dates_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
