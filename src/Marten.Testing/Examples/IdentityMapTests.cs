using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace Marten.Testing.Examples;

public class IdentityMapTests: IntegrationContext
{
    public void using_identity_map()
    {
        #region sample_using-identity-map
        var user = new User { FirstName = "Tamba", LastName = "Hali" };
        theStore.BulkInsert(new[] { user });

        // Open a document session with the identity map
        using var session = theStore.IdentitySession();
        session.Load<User>(user.Id)
            .ShouldBeSameAs(session.Load<User>(user.Id));
        #endregion
    }

    public IdentityMapTests(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
