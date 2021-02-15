using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace Marten.Testing.Examples
{
    public class IdentityMapTests: IntegrationContext
    {
        #region sample_using-identity-map
        public void using_identity_map()
        {
            var user = new User { FirstName = "Tamba", LastName = "Hali" };
            theStore.BulkInsert(new[] { user });

            // Open a document session with the identity map
            using (var session = theStore.OpenSession())
            {
                session.Load<User>(user.Id)
                    .ShouldBeTheSameAs(session.Load<User>(user.Id));
            }
        }

        #endregion sample_using-identity-map
        public IdentityMapTests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
