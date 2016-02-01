using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Services
{
    public class BatchedSamples : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public async Task sample_usage()
        {
           

            // Start a new batched query
            var batch = theSession.CreateBatchQuery();

            // Set up some queries that you'll want the results for

            // Load doc by some id
            var userTask = batch.Load<User>(Guid.NewGuid());

            // load several by some id
            var usersTask = batch.Load<User>().ById(Guid.NewGuid(), Guid.Empty, Guid.NewGuid());

            // load a query
            var userByName = batch.Query<User>().For(_ => _.Where(x => x.FirstName == "Jeremy").ToList());

            // Send the batch all at once in a single network request to the DB
            await batch.Execute();

            // work with the results
            Debug.WriteLine(userTask.Result);
            Debug.WriteLine(usersTask.Result.Count());
            Debug.WriteLine(userByName.Result.Count);
        }
    }
}