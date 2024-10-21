using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.HierarchicalStorage;

public class end_to_end_document_hierarchy_usage_Tests: OneOffConfigurationsContext
{
    protected AdminUser admin1 = new AdminUser
    {
        UserName = "A2", FirstName = "Derrick", LastName = "Johnson", Region = "Midwest"
    };

    protected AdminUser admin2 = new AdminUser
    {
        UserName = "B2", FirstName = "Eric", LastName = "Berry", Region = "West Coast"
    };

    protected SuperUser super1 = new SuperUser
    {
        UserName = "A3", FirstName = "Dontari", LastName = "Poe", Role = "Expert"
    };

    protected SuperUser super2 = new SuperUser
    {
        UserName = "B3", FirstName = "Sean", LastName = "Smith", Role = "Master"
    };

    protected User user1 = new User { UserName = "A1", FirstName = "Justin", LastName = "Houston" };
    protected User user2 = new User { UserName = "B1", FirstName = "Tamba", LastName = "Hali" };

    protected end_to_end_document_hierarchy_usage_Tests()
    {
        StoreOptions(
            _ =>
            {
                _.Schema.For<User>().AddSubClass<SuperUser>().AddSubClass<AdminUser>().Duplicate(x => x.UserName);
            });
    }

    protected async Task loadData()
    {
        theSession.Store(user1, user2, admin1, admin2, super1, super2);

        await theSession.SaveChangesAsync();
    }


    protected async Task<IDocumentSession> identitySessionWithData()
    {
        var session = theStore.IdentitySession();
        session.Store(user1, user2, admin1, admin2, super1, super2);

        await session.SaveChangesAsync();
        return session;
    }
}
