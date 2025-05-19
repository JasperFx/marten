using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_635_operations_order_in_same_session: IntegrationContext
{
    [Fact]
    public async Task deletewhere_and_store()
    {
        var batchSize = 256;

        for (var i = 0; i < batchSize; i++)
        {
            theSession.Store(i % 2 == 0 ? new User { LastName = "batch-id1" } : new User { LastName = "batch-id2" });
        }

        await theSession.SaveChangesAsync();

        var batch = new List<User>();
        var newBatchSize = 2;
        for (var i = 0; i < newBatchSize; i++)
        {
            batch.Add(new User { LastName = "batch-id1" });
        }

        using (var replaceSession = theStore.LightweightSession())
        {
            //First, delete everything matching a given criteria
            replaceSession.DeleteWhere<User>(x => x.LastName == "batch-id1");

            //Then store all new documents, in this case they also match the delete criteria
            replaceSession.Store(batch.ToArray());

            await replaceSession.SaveChangesAsync();
        }

        using (var querySession = theStore.QuerySession())
        {
            var count = querySession.Query<User>().Count(x => x.LastName == "batch-id1");

            //This fails as the DeleteWhere gets executed after the Store(...) in the replaceSession
            count.ShouldBe(newBatchSize);
        }
    }

    public Bug_635_operations_order_in_same_session(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
