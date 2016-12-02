using System.Collections.Generic;
using Marten.Services;
using System.Linq;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class deletewhere_and_store_in_same_session : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void should_be_able_to_deletewhere_and_store_in_same_session()
        {
            int batchSize = 510;

            var batch = new List<User>();
            for (int i = 0; i < batchSize; i++)
            {
                batch.Add(new User {LastName = "batch-id"});
            }

            theSession.DeleteWhere<User>(x => x.LastName == "batch-id");
            theSession.Store(batch.ToArray());
            theSession.SaveChanges();


            using (var session = theStore.QuerySession())
            {
                var count = session.Query<User>().Count(x => x.LastName == "batch-id");
                count.ShouldBe(batchSize);
            }
        }
    }
}