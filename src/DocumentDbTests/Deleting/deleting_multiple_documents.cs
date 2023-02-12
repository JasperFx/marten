using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Deleting;

public class deleting_multiple_documents : IntegrationContext
{
    [Theory]
    [SessionTypes]
    public void multiple_documents(DocumentTracking tracking)
    {
        DocumentTracking = tracking;

        #region sample_mixed-docs-to-store
        var user1 = new User {FirstName = "Jeremy", LastName = "Miller"};
        var issue1 = new Issue {Title = "TV won't turn on"}; // unfortunately true as I write this...
        var company1 = new Company{Name = "Widgets, inc."};
        var company2 = new Company{Name = "BigCo"};
        var company3 = new Company{Name = "SmallCo"};

        theSession.Store<object>(user1, issue1, company1, company2, company3);
        #endregion

        theSession.SaveChanges();

        using (var session = theStore.LightweightSession())
        {
            var user = session.Load<User>(user1.Id);
            user.FirstName = "Max";

            session.Store(user);

            session.Delete(company2);

            session.SaveChanges();
        }

        using (var session = theStore.QuerySession())
        {
            session.Load<User>(user1.Id).FirstName.ShouldBe("Max");
            session.Load<Company>(company1.Id).Name.ShouldBe("Widgets, inc.");
            session.Load<Company>(company2.Id).ShouldBeNull();
            session.Load<Company>(company3.Id).Name.ShouldBe("SmallCo");
        }
    }

    [Theory]
    [SessionTypes]
    public void delete_multiple_types_of_documents_with_delete_objects(DocumentTracking tracking)
    {
        DocumentTracking = tracking;

        #region sample_DeleteObjects
        // Store a mix of different document types
        var user1 = new User { FirstName = "Jamie", LastName = "Vaughan" };
        var issue1 = new Issue { Title = "Running low on coffee" };
        var company1 = new Company { Name = "ECorp" };

        theSession.StoreObjects(new object[] { user1, issue1, company1 });

        theSession.SaveChanges();

        // Delete a mix of documents types
        using (var session = theStore.LightweightSession())
        {
            session.DeleteObjects(new object[] { user1, company1 });

            session.SaveChanges();
        }
        #endregion

        using (var session = theStore.QuerySession())
        {
            // Assert the deleted documents no longer exist
            session.Load<User>(user1.Id).ShouldBeNull();
            session.Load<Company>(company1.Id).ShouldBeNull();

            session.Load<Issue>(issue1.Id).Title.ShouldBe("Running low on coffee");
        }
    }

    public deleting_multiple_documents(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
