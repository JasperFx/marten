using System;
using System.Linq;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Linq
{
    public class invoking_queryable_through_last_Tests: DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void last_throws_an_exception()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.SaveChanges();

            Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
            {
                theSession.Query<Target>().Last(x => x.Number == 3)
                    .ShouldNotBeNull();
            });
        }
    }
}
