using Octokit;
using StructureMap;
using User = Marten.Testing.Documents.User;

namespace Marten.Testing.Examples
{
    public class IdentityMapTests
    {
        // SAMPLE: using-identity-map
        public void using_identity_map()
        {
            var container = Container.For<DevelopmentModeRegistry>();
            var store = container.GetInstance<IDocumentStore>();

            var user = new User {FirstName = "Tamba", LastName = "Hali"};
            store.BulkInsert(new [] {user});

            // Open a document session with the identity map
            using (var session = store.OpenSession())
            {
                session.Load<User>(user.Id)
                    .ShouldBeTheSameAs(session.Load<User>(user.Id));
            }
        } 
        // ENDSAMPLE
    }
}