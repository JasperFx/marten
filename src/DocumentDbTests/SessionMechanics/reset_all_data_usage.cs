using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.SessionMechanics
{
    public class reset_all_data_usage : OneOffConfigurationsContext
    {
        private readonly ITestOutputHelper _output;

        public reset_all_data_usage(ITestOutputHelper output)
        {
            _output = output;
        }

        public class Users : IInitialData
        {
            public Task Populate(IDocumentStore store, CancellationToken cancellation)
            {
                var users = new User[]
                {
                    new User { UserName = "one" }, new User { UserName = "two" }, new User { UserName = "three" },
                    new User { UserName = "four" },
                };

                return store.BulkInsertDocumentsAsync(users, cancellation: cancellation);
            }
        }

        [Fact]
        public async Task reset_clears_and_sets_the_baseline()
        {
            StoreOptions(opts =>
            {
                opts.Logger(new TestOutputMartenLogger(_output));
            });

            #region sample_reset_all_data
            theStore.Advanced.InitialDataCollection.Add(new Users());

            await theStore.Advanced.ResetAllData();
            #endregion

            using (var session = theStore.LightweightSession())
            {
                var user = new User { UserName = "five" };
                session.Store(user);

                var one = await session.Query<User>().Where(x => x.UserName == "one")
                    .FirstOrDefaultAsync();

                session.Delete(one);

                await session.SaveChangesAsync();
            }

            await theStore.Advanced.ResetAllData();

            using var query = theStore.QuerySession();

            var names = await query.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName).ToListAsync();

            names.ShouldHaveTheSameElementsAs("four", "one", "three", "two");


        }
    }
}
