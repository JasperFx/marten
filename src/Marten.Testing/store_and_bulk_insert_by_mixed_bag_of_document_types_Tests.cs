using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class store_and_bulk_insert_by_mixed_bag_of_document_types_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void store_multiple_types_of_documents_at_one_time()
        {
            var user1 = new User();
            var user2 = new User();
            var issue1 = new Issue();
            var issue2 = new Issue();
            var company1 = new Company();
            var company2 = new Company();

            theSession.Store<object>(user1, user2, issue1, issue2, company1, company2);
            theSession.SaveChanges();

            using (var querying = theStore.QuerySession())
            {
                querying.Query<User>().Count().ShouldBe(2);
                querying.Query<Issue>().Count().ShouldBe(2);
                querying.Query<Company>().Count().ShouldBe(2);
            }
        }

        [Fact]
        public void store_multiple_types_of_documents_at_one_time_by_StoreObjects()
        {
            var user1 = new User();
            var user2 = new User();
            var issue1 = new Issue();
            var issue2 = new Issue();
            var company1 = new Company();
            var company2 = new Company();

            var documents = new object[] { user1, user2, issue1, issue2, company1, company2};
            theSession.StoreObjects(documents);
            theSession.SaveChanges();

            using (var querying = theStore.QuerySession())
            {
                querying.Query<User>().Count().ShouldBe(2);
                querying.Query<Issue>().Count().ShouldBe(2);
                querying.Query<Company>().Count().ShouldBe(2);
            }
        }

        [Fact]
        public void can_bulk_insert_mixed_list_of_objects()
        {
            var user1 = new User();
            var user2 = new User();
            var issue1 = new Issue();
            var issue2 = new Issue();
            var company1 = new Company();
            var company2 = new Company();

            var documents = new object[] { user1, user2, issue1, issue2, company1, company2 };

            theStore.BulkInsert(documents);

            using (var querying = theStore.QuerySession())
            {
                querying.Query<User>().Count().ShouldBe(2);
                querying.Query<Issue>().Count().ShouldBe(2);
                querying.Query<Company>().Count().ShouldBe(2);
            }
        }

        [Fact]
        public void can_bulk_insert_mixed_list_of_objects_by_objects()
        {
            var user1 = new User();
            var user2 = new User();
            var issue1 = new Issue();
            var issue2 = new Issue();
            var company1 = new Company();
            var company2 = new Company();

            var documents = new object[] { user1, user2, issue1, issue2, company1, company2 };

            theStore.BulkInsertDocuments(documents);

            using (var querying = theStore.QuerySession())
            {
                querying.Query<User>().Count().ShouldBe(2);
                querying.Query<Issue>().Count().ShouldBe(2);
                querying.Query<Company>().Count().ShouldBe(2);
            }
        }
    }
}