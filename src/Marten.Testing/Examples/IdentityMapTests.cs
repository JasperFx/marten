using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace Marten.Testing.Examples;

public class IdentityMapTests: IntegrationContext
{
    public async Task using_identity_map()
    {
        #region sample_using-identity-map
        var user = new User { FirstName = "Tamba", LastName = "Hali" };
        await theStore.BulkInsertAsync(new[] { user });

        // Open a document session with the identity map
        using var session = theStore.IdentitySession();
        (await session.LoadAsync<User>(user.Id))
            .ShouldBeSameAs(await session.LoadAsync<User>(user.Id));
        #endregion
    }

    public IdentityMapTests(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
