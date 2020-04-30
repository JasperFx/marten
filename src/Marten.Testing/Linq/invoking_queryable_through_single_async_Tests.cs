using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Linq
{
    [SingleStoryteller]
    public class invoking_queryable_through_single_async_Tests: IntegrationContextWithIdentityMap<NulloIdentityMap>
    {
        [Fact]
        public async Task single_hit_with_only_one_document()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var target = await theSession.Query<Target>().SingleAsync(x => x.Number == 3).ConfigureAwait(false);
            target.ShouldNotBeNull();
        }

        [Fact]
        public async Task single_or_default_hit_with_only_one_document()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var target = await theSession.Query<Target>().SingleOrDefaultAsync(x => x.Number == 3).ConfigureAwait(false);
            target.ShouldNotBeNull();
        }

        [Fact]
        public async Task single_or_default_miss()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var target = await theSession.Query<Target>().SingleOrDefaultAsync(x => x.Number == 11).ConfigureAwait(false);
            target.ShouldBeNull();
        }

        [Fact]
        public async Task single_hit_with_more_than_one_match()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            await Exception<InvalidOperationException>.ShouldBeThrownByAsync(async () =>
            {
                await theSession.Query<Target>().Where(x => x.Number == 2).SingleAsync().ConfigureAwait(false);
            });
        }

        [Fact]
        public async Task single_or_default_hit_with_more_than_one_match()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            await Exception<InvalidOperationException>.ShouldBeThrownByAsync(async () =>
            {
                await theSession.Query<Target>().Where(x => x.Number == 2).SingleOrDefaultAsync().ConfigureAwait(false);
            });
        }

        [Fact]
        public async Task single_miss()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            await Exception<InvalidOperationException>.ShouldBeThrownByAsync(async () =>
            {
                await theSession.Query<Target>().Where(x => x.Number == 11).SingleAsync().ConfigureAwait(false);
            });
        }

        public invoking_queryable_through_single_async_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
