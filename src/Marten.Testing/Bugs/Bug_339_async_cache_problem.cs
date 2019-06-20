using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_339_async_cache_problem: DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public async Task pending_with_dirty_checks_async()
        {
            var user1 = new User();

            using (var session1 = theStore.LightweightSession())
            {
                session1.Store(user1);
                session1.SaveChanges();
            }

            using (var session2 = theStore.DirtyTrackedSession())
            {
                var user12 = await session2.LoadAsync<User>(user1.Id).ConfigureAwait(false);
                var breakThings = await session2.LoadAsync<User>(user1.Id).ConfigureAwait(false);
            }
        }
    }
}
