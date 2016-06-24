using System;
using System.Linq;
using Marten.Services;
using Marten.Testing.Fixtures;
using Xunit;
using System.Threading.Tasks;
using Marten.Util;

namespace Marten.Testing.Linq
{
    public class invoking_queryable_through_first_async_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public async Task first_hit_with_only_one_document()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var target = await theSession.Query<Target>().FirstAsync(x => x.Number == 3).ConfigureAwait(false);
            target.ShouldNotBeNull();
        }

        [Fact]
        public async Task first_or_default_hit_with_only_one_document()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var target = await theSession.Query<Target>().FirstOrDefaultAsync(x => x.Number == 3).ConfigureAwait(false);
            target.ShouldNotBeNull();
        }

        [Fact]
        public async Task first_or_default_miss()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var target = await theSession.Query<Target>().FirstOrDefaultAsync(x => x.Number == 11).ConfigureAwait(false);
            target.ShouldBeNull();
        }

        [Fact]
        public async Task first_correct_hit_with_more_than_one_match()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2, Flag = true });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var target = await theSession.Query<Target>().Where(x => x.Number == 2).FirstAsync().ConfigureAwait(false);
            target.Flag.ShouldBeTrue();
        }

        [Fact]
        public async Task first_or_default_correct_hit_with_more_than_one_match()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2, Flag = true });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var target = await theSession.Query<Target>().Where(x => x.Number == 2).FirstOrDefaultAsync().ConfigureAwait(false);
            target.Flag.ShouldBeTrue();
        }

        [Fact]
        public async Task first_miss()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            await Exception<InvalidOperationException>.ShouldBeThrownByAsync(async () =>
            {
                await theSession.Query<Target>().Where(x => x.Number == 11).FirstAsync().ConfigureAwait(false);
            });
        }
    }
}