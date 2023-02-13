using System;
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
    public void multiple_documents(DocumentTracking tracking)
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

        session.SaveChanges();

        using (var documentSession = theStore.LightweightSession())
        {
            var user = documentSession.Load<User>(user1.Id);
            user.FirstName = "Max";

            documentSession.Store(user);

            documentSession.Delete(company2);

            documentSession.SaveChanges();
        }

        using (var querySession = theStore.QuerySession())
        {
            querySession.Load<User>(user1.Id).FirstName.ShouldBe("Max");
            querySession.Load<Company>(company1.Id).Name.ShouldBe("Widgets, inc.");
            querySession.Load<Company>(company2.Id).ShouldBeNull();
            querySession.Load<Company>(company3.Id).Name.ShouldBe("SmallCo");
        }
    }

    [Theory]
    [SessionTypes]
    public void delete_multiple_types_of_documents_with_delete_objects(DocumentTracking tracking)
    {
        using var session = OpenSession(tracking);

        #region sample_DeleteObjects

        // Store a mix of different document types
        var user1 = new User { FirstName = "Jamie", LastName = "Vaughan" };
        var issue1 = new Issue { Title = "Running low on coffee" };
        var company1 = new Company { Name = "ECorp" };

        session.StoreObjects(new object[] { user1, issue1, company1 });

        session.SaveChanges();

        // Delete a mix of documents types
        using (var documentSession = theStore.LightweightSession())
        {
            documentSession.DeleteObjects(new object[] { user1, company1 });

            documentSession.SaveChanges();
        }

        #endregion

        using (var querySession = theStore.QuerySession())
        {
            // Assert the deleted documents no longer exist
            querySession.Load<User>(user1.Id).ShouldBeNull();
            querySession.Load<Company>(company1.Id).ShouldBeNull();

            querySession.Load<Issue>(issue1.Id).Title.ShouldBe("Running low on coffee");
        }
    }

    public deleting_multiple_documents(DefaultStoreFixture fixture): base(fixture)
    {
    }
}
