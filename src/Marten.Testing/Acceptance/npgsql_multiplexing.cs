using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class npgsql_multiplexing : IntegrationContext
    {
        private readonly string _connectionMultiplexed = $"{ConnectionSource.ConnectionString};multiplexing=true";

        [Fact]
        public async Task can_insert_documents()
        {
            using var store = DocumentStore.For(options =>
            {
                options.Connection(_connectionMultiplexed);
                options.DatabaseSchemaName = "multiplex";
            });

            await store.Advanced.Clean.CompletelyRemoveAsync(typeof(Target));

            await using (var session = store.OpenSession())
            {
                session.Insert(Target.GenerateRandomData(99).ToArray());
                await session.SaveChangesAsync();
            }

            await using (var query = store.QuerySession())
            {
                (await query.Query<Target>().CountAsync()).ShouldBe(99);
            }
        }

        public npgsql_multiplexing(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
