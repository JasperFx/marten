using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Deleting;

public class deleting_multiple_documents: IntegrationContext
{
    [Theory]
    [SessionTypes]
    public async Task multiple_documents(DocumentTracking tracking)
    {
        using var session = OpenSession(tracking);

        #region sample_mixed-docs-to-store

        var user1 = new User { FirstName = "Jeremy", LastName = "Miller" };
        var issue1 = new Issue { Title = "TV won't turn on" }; // unfortunately true as I write this...
        var company1 = new Company { Name = "Widgets, inc." };
        var company2 = new Company { Name = "BigCo" };
        var company3 = new Company { Name = "SmallCo" };

        session.Store<object>(user1, issue1, company1, company2, company3);

        #endregion

        await session.SaveChangesAsync();

        using (var documentSession = theStore.LightweightSession())
        {
            var user = await documentSession.LoadAsync<User>(user1.Id);
            user.FirstName = "Max";

            documentSession.Store(user);

            documentSession.Delete(company2);

            await documentSession.SaveChangesAsync();
        }

        using (var querySession = theStore.QuerySession())
        {
            (await querySession.LoadAsync<User>(user1.Id)).FirstName.ShouldBe("Max");
            (await querySession.LoadAsync<Company>(company1.Id)).Name.ShouldBe("Widgets, inc.");
            (await querySession.LoadAsync<Company>(company2.Id)).ShouldBeNull();
            (await querySession.LoadAsync<Company>(company3.Id)).Name.ShouldBe("SmallCo");
        }
    }

    [Theory]
    [SessionTypes]
    public async Task delete_multiple_types_of_documents_with_delete_objects(DocumentTracking tracking)
    {
        using var session = OpenSession(tracking);

        #region sample_DeleteObjects

        // Store a mix of different document types
        var user1 = new User { FirstName = "Jamie", LastName = "Vaughan" };
        var issue1 = new Issue { Title = "Running low on coffee" };
        var company1 = new Company { Name = "ECorp" };

        session.StoreObjects(new object[] { user1, issue1, company1 });

        await session.SaveChangesAsync();

        // Delete a mix of documents types
        using (var documentSession = theStore.LightweightSession())
        {
            documentSession.DeleteObjects(new object[] { user1, company1 });

            await documentSession.SaveChangesAsync();
        }

        #endregion

        using (var querySession = theStore.QuerySession())
        {
            // Assert the deleted documents no longer exist
            (await querySession.LoadAsync<User>(user1.Id)).ShouldBeNull();
            (await querySession.LoadAsync<Company>(company1.Id)).ShouldBeNull();

            (await querySession.LoadAsync<Issue>(issue1.Id)).Title.ShouldBe("Running low on coffee");
        }
    }

    public deleting_multiple_documents(DefaultStoreFixture fixture): base(fixture)
    {
    }
}
