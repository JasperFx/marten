using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Fixtures;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class invoking_queryable_count_async_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public async Task count_without_any_where()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync();

            var result = await theSession.Query<Target>().CountAsync();
            result.ShouldBe(4);
        }

        [Fact]
        public async Task long_count_without_any_where()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync();

            var result = await theSession.Query<Target>().LongCountAsync();
            result.ShouldBe(4);
        }

        [Fact]
        public async Task count_with_a_where_clause()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.Store(new Target { Number = 5 });
            theSession.Store(new Target { Number = 6 });
            await theSession.SaveChangesAsync();

            var result = await theSession.Query<Target>().CountAsync(x => x.Number > 3);
            result.ShouldBe(3);
        }

        [Fact]
        public async Task long_count_with_a_where_clause()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.Store(new Target { Number = 5 });
            theSession.Store(new Target { Number = 6 });
            await theSession.SaveChangesAsync();

            var result = await theSession.Query<Target>().LongCountAsync(x => x.Number > 3);
            result.ShouldBe(3);
        }
    }
}