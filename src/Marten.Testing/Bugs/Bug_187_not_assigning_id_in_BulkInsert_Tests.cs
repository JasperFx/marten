using System.Linq;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_187_not_assigning_id_in_BulkInsert_Tests : IntegratedFixture
    {
        [Fact]
        public void does_indeed_assign_the_id_during_bulk_insert()
        {
            var docs = new IntDoc[50];
            for (var i = 0; i < docs.Length; i++)
            {
                docs[i] = new IntDoc();
            }

            theStore.BulkInsert(docs);

            using (var session = theStore.QuerySession())
            {
                session.Query<IntDoc>().Count().ShouldBe(50);
            }
        }
    }
}