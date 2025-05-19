using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_339_async_cache_problem: IntegrationContext
{
    [Fact]
    public async Task pending_with_dirty_checks_async()
    {
        var user1 = new User();

        await using (var session1 = theStore.LightweightSession())
        {
            session1.Store(user1);
            await session1.SaveChangesAsync();
        }

        await using (var session2 = theStore.DirtyTrackedSession())
        {
            var user12 = await session2.LoadAsync<User>(user1.Id);
            var breakThings = await session2.LoadAsync<User>(user1.Id);
        }
    }

    public Bug_339_async_cache_problem(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
