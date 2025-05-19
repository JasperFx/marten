using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity.Sequences;

public class IdentityKeyGenerationTests : OneOffConfigurationsContext
{
    [Fact]
    public async Task When_documents_are_stored_after_each_other_then_the_first_id_should_be_less_than_the_second()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<UserWithString>().UseIdentityKey();
        });

        await StoreUser(theStore, "User1");
        await StoreUser(theStore, "User2");
        await StoreUser(theStore, "User3");

        var users = GetUsers(theStore);

        GetId(users, "User1").ShouldBe("userwithstring/1");
        GetId(users, "User2").ShouldBe("userwithstring/2");
        GetId(users, "User3").ShouldBe("userwithstring/3");
    }

    #region sample_DocumentWithStringId

    public class DocumentWithStringId
    {
        public string Id { get; set; }
    }

    #endregion

    private void sample_usage()
    {
        #region sample_using_IdentityKey

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");
            opts.Schema.For<DocumentWithStringId>()
                .UseIdentityKey()
                .DocumentAlias("doc");
        });

        #endregion
    }

    private static string GetId(UserWithString[] users, string user1)
    {
        return users.Single(user => user.LastName == user1).Id;
    }

    private static UserWithString[] GetUsers(IDocumentStore documentStore)
    {
        using var session = documentStore.QuerySession();
        return session.Query<UserWithString>().ToArray();
    }

    private static async Task StoreUser(IDocumentStore documentStore, string lastName)
    {
        using var session = documentStore.IdentitySession();
        session.Store(new UserWithString { LastName = lastName});
        await session.SaveChangesAsync();
    }

}
