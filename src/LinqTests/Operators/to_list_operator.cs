using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

public class to_list_operator : IntegrationContext
{
    #region sample_using-to-list-async
    [Fact]
    public async Task use_to_list_async_in_query()
    {
        theSession.Store(new User { FirstName = "Hank" });
        theSession.Store(new User { FirstName = "Bill" });
        theSession.Store(new User { FirstName = "Sam" });
        theSession.Store(new User { FirstName = "Tom" });

        await theSession.SaveChangesAsync();

        var users = await theSession
            .Query<User>()
            .Where(x => x.FirstName == "Sam")
            .ToListAsync();

        users.Single().FirstName.ShouldBe("Sam");
    }
    #endregion

    [Fact]
    public async Task should_return_empty_list()
    {
        var users = await theSession
            .Query<User>()
            .Where(x => x.FirstName == "Sam")
            .ToListAsync();
        users.ShouldBeEmpty();
    }

    public to_list_operator(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
