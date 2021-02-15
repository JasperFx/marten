using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    [MatchingStoryteller]
    public class invoking_queryable_through_to_list_async_Tests : IntegrationContext
    {
        #region sample_using-to-list-async
        [Fact]
        public async Task use_to_list_async_in_query()
        {
            theSession.Store(new User { FirstName = "Hank" });
            theSession.Store(new User { FirstName = "Bill" });
            theSession.Store(new User { FirstName = "Sam" });
            theSession.Store(new User { FirstName = "Tom" });

            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var users = await theSession
                .Query<User>()
                .Where(x => x.FirstName == "Sam")
                .ToListAsync().ConfigureAwait(false);

            users.Single().FirstName.ShouldBe("Sam");
        }
        #endregion sample_using-to-list-async

        [Fact]
        public async Task should_return_empty_list()
        {
            var users = await theSession
                .Query<User>()
                .Where(x => x.FirstName == "Sam")
                .ToListAsync().ConfigureAwait(false);
            users.ShouldBeEmpty();
        }

        public invoking_queryable_through_to_list_async_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
