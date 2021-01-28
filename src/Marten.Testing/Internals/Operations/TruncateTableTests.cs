using System.Linq;
using System.Threading.Tasks;
using Marten.Internal.Operations;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Internals.Operations
{
    public class TruncateTableTests : IntegrationContext
    {
        public TruncateTableTests(DefaultStoreFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task truncate_by_document()
        {
            var targets = Target.GenerateRandomData(100).ToList();
            await theStore.BulkInsertAsync(targets);

            var op = new TruncateTable(typeof(Target));
            theSession.QueueOperation(op);
            await theSession.SaveChangesAsync();

            var count = await theSession.Query<Target>().CountAsync();
            count.ShouldBe(0);
        }

        [Fact]
        public async Task truncate_by_table_name()
        {
            var targets = Target.GenerateRandomData(100).ToList();
            await theStore.BulkInsertAsync(targets);

            var tableName = theStore.Options.Storage.MappingFor(typeof(Target)).TableName;
            var op = new TruncateTable(tableName);
            theSession.QueueOperation(op);
            await theSession.SaveChangesAsync();

            var count = await theSession.Query<Target>().CountAsync();
            count.ShouldBe(0);
        }
    }
}
