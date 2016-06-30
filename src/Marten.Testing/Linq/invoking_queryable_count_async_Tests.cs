using System.Threading.Tasks;
using Marten.Services;
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
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var result = await theSession.Query<Target>().CountAsync().ConfigureAwait(false);
            result.ShouldBe(4);
        }

        [Fact]
        public async Task long_count_without_any_where()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var result = await theSession.Query<Target>().LongCountAsync().ConfigureAwait(false);
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
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var result = await theSession.Query<Target>().CountAsync(x => x.Number > 3).ConfigureAwait(false);
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
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var result = await theSession.Query<Target>().LongCountAsync(x => x.Number > 3).ConfigureAwait(false);
            result.ShouldBe(3);
        }

        [Fact]
        public async Task sum_without_any_where()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var result = await theSession.Query<Target>().SumAsync(x => x.Number).ConfigureAwait(false);
            result.ShouldBe(10);
        }

        [Fact]
        public async Task sum_with_nullable()
        {
            theSession.Store(new Target { NullableNumber = 1 });
            theSession.Store(new Target { NullableNumber = 2 });
            theSession.Store(new Target { NullableNumber = 3 });
            theSession.Store(new Target { NullableNumber = 4 });
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var result = await theSession.Query<Target>().SumAsync(x => x.NullableNumber).ConfigureAwait(false);
            result.ShouldBe(10);
        }
    }
}