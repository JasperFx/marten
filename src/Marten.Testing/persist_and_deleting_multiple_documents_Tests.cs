using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;

namespace Marten.Testing
{
    public class persist_and_deleting_multiple_documents_with_Nullo_Tests : persist_and_deleting_multiple_documents_Tests<NulloIdentityMap> { }
    public class persist_and_deleting_multiple_documents_with_IdentityMap_Tests : persist_and_deleting_multiple_documents_Tests<IdentityMap> { }
    public class persist_and_deleting_multiple_documents_with_DirtyTracking_Tests : persist_and_deleting_multiple_documents_Tests<DirtyTrackingIdentityMap> { }

    public class persist_and_deleting_multiple_documents_Tests<T> : DocumentSessionFixture<T> where T : IIdentityMap
    {
        public void multiple_documents()
        {
            var user1 = new User {FirstName = "Jeremy", LastName = "Miller"};
            var issue1 = new Issue {Title = "TV won't turn on"}; // unfortunately true as I write this...
            var company1 = new Company{Name = "Widgets, inc."};
            var company2 = new Company{Name = "BigCo"};
            var company3 = new Company{Name = "SmallCo"};

            theSession.Store(user1);
            theSession.Store(issue1);
            theSession.Store(company1);
            theSession.Store(company2);
            theSession.Store(company3);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                var user = session.Load<User>(user1.Id);
                user.FirstName = "Max";

                session.Store(user);

                session.Delete(company2);

                session.SaveChanges();
            }

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Load<User>(user1.Id).FirstName.ShouldBe("Max");
                session.Load<Company>(company1.Id).Name.ShouldBe("Widgets, inc.");
                session.Load<Company>(company2.Id).ShouldBeNull();
                session.Load<Company>(company3.Id).Name.ShouldBe("SmallCo");
            }
        }
    }
}