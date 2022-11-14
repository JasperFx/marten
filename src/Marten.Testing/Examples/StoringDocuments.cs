using System.Threading.Tasks;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples;

public class StoringDocuments
{
    internal async Task sample_storing_multiple_types_of_documents()
    {
        #region sample_store_mixed_bag_of_document_types

        using var store = DocumentStore.For("some connection string");
        var user1 = new User();
        var user2 = new User();
        var issue1 = new Issue();
        var issue2 = new Issue();
        var company1 = new Company();
        var company2 = new Company();

        await using var session = store.LightweightSession();

        session.Store<object>(user1, user2, issue1, issue2, company1, company2);
        await session.SaveChangesAsync();

        // Or this usage:
        var documents = new object[] {user1, user2, issue1, issue2, company1, company2};

        // The argument here is any kind of IEnumerable<object>
        session.StoreObjects(documents);
        await session.SaveChangesAsync();

        #endregion
    }

    public async Task using_store()
    {
        #region sample_using_DocumentSession_Store

        using var store = DocumentStore.For("some connection string");

        await using var session = store.LightweightSession();

        var newUser = new User
        {
            UserName = "travis.kelce"
        };

        var existingUser = await session.Query<User>()
            .SingleAsync(x => x.UserName == "patrick.mahomes");

        existingUser.Roles = new[] {"admin"};

        // We're storing one brand new document, and one
        // existing document that will just be replaced
        // upon SaveChangesAsync()
        session.Store(newUser, existingUser);

        await session.SaveChangesAsync();

        #endregion

    }
}