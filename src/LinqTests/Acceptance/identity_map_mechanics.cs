using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Acceptance;

public class identity_map_mechanics: IntegrationContext
{
    private User user1;
    private User user2;
    private User user3;
    private User user4;

    public identity_map_mechanics(DefaultStoreFixture fixture): base(fixture)
    {
    }

    private async Task<IDocumentSession> identitySessionWithData()
    {
        #region sample_using-store-with-multiple-docs

        user1 = new User { FirstName = "Jeremy" };
        user2 = new User { FirstName = "Jens" };
        user3 = new User { FirstName = "Jeff" };
        user4 = new User { FirstName = "Corey" };

        var session = theStore.IdentitySession();
        session.Store(user1, user2, user3, user4);

        #endregion

        await session.SaveChangesAsync();

        (await session.LoadAsync<User>(user1.Id)).ShouldBeSameAs(user1);

        return session;
    }

    [Fact]
    public async Task single_runs_through_the_identity_map()
    {
        await using var session = await identitySessionWithData();
        session.Query<User>()
            .Single(x => x.FirstName == "Jeremy").ShouldBeSameAs(user1);

        session.Query<User>()
            .SingleOrDefault(x => x.FirstName == user4.FirstName).ShouldBeSameAs(user4);
    }


    [Fact]
    public async Task first_runs_through_the_identity_map()
    {
        await using var session = await identitySessionWithData();
        session.Query<User>().Where(x => x.FirstName.StartsWith("J")).OrderBy(x => x.FirstName)
            .First().ShouldBeSameAs(user3);


        session.Query<User>().Where(x => x.FirstName.StartsWith("J")).OrderBy(x => x.FirstName)
            .FirstOrDefault().ShouldBeSameAs(user3);
    }

    [Fact]
    public async Task query_runs_through_identity_map()
    {
        await using var session = await identitySessionWithData();
        var users = session.Query<User>().Where(x => x.FirstName.StartsWith("J")).OrderBy(x => x.FirstName)
            .ToArray();

        users[0].ShouldBeSameAs(user3);
        users[1].ShouldBeSameAs(user2);
        users[2].ShouldBeSameAs(user1);
    }

    [Fact]
    public async Task single_runs_through_the_identity_map_async()
    {
        await using var session = await identitySessionWithData();
        var u1 = await session.Query<User>().Where(x => x.FirstName == "Jeremy")
            .SingleAsync();

        u1.ShouldBeSameAs(user1);

        var u2 = await session.Query<User>().Where(x => x.FirstName == user4.FirstName)
            .SingleOrDefaultAsync();

        u2.ShouldBeSameAs(user4);
    }


    [Fact]
    public async Task first_runs_through_the_identity_map_async()
    {
        await using var session = await identitySessionWithData();
        var u1 = await session.Query<User>().Where(x => x.FirstName.StartsWith("J")).OrderBy(x => x.FirstName)
            .FirstAsync();

        u1.ShouldBeSameAs(user3);


        var u2 = await session.Query<User>().Where(x => x.FirstName.StartsWith("J")).OrderBy(x => x.FirstName)
            .FirstOrDefaultAsync();

        u2.ShouldBeSameAs(user3);
    }


    [Fact]
    public async Task query_runs_through_identity_map_async()
    {
        await using var session = await identitySessionWithData();
        var users = await session.Query<User>().Where(x => x.FirstName.StartsWith("J")).OrderBy(x => x.FirstName)
            .ToListAsync();

        users[0].ShouldBeSameAs(user3);
        users[1].ShouldBeSameAs(user2);
        users[2].ShouldBeSameAs(user1);
    }
}
