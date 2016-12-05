using System.Collections.Generic;
using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class operations_order_in_same_session : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void deletewhere_and_store()
        {
            var batchSize = 256;

            for (var i = 0; i < batchSize; i++)
            {
                theSession.Store(i % 2 == 0 ? new User { LastName = "batch-id1" } : new User { LastName = "batch-id2" });
            }

            theSession.SaveChanges();

            var batch = new List<User>();
            var newBatchSize = 2;
            for (var i = 0; i < newBatchSize; i++)
            {
                batch.Add(new User { LastName = "batch-id1" });
            }

            using (var replaceSession = theStore.OpenSession())
            {
                //First, delete everything matching a given criteria
                replaceSession.DeleteWhere<User>(x => x.LastName == "batch-id1");

                //Then store all new documents, in this case they also match the delete criteria
                replaceSession.Store(batch.ToArray());

                replaceSession.SaveChanges();
            }

            using (var querySession = theStore.QuerySession())
            {
                var count = querySession.Query<User>().Count(x => x.LastName == "batch-id1");

                //This fails as the DeleteWhere gets executed after the Store(...) in the replaceSession
                count.ShouldBe(newBatchSize);
            }
        }
    }
}