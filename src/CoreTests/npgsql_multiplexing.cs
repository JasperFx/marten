using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests
{
    public class npgsql_multiplexing : OneOffConfigurationsContext
    {
        private readonly string _connectionMultiplexed = $"{ConnectionSource.ConnectionString};multiplexing=true";

        [Fact]
        public async Task can_insert_documents()
        {
            StoreOptions(options =>
            {
                options.Connection(_connectionMultiplexed);
            });

            await theStore.Advanced.Clean.CompletelyRemoveAsync(typeof(Target));

            await using (var session = theStore.OpenSession())
            {
                session.Insert(Target.GenerateRandomData(99).ToArray());
                await session.SaveChangesAsync();
            }

            await using (var query = theStore.QuerySession())
            {
                (await query.Query<Target>().CountAsync()).ShouldBe(99);
            }
        }

    }
}
