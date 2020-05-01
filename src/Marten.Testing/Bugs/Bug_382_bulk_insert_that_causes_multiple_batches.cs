using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_382_bulk_insert_that_causes_multiple_batches: BugIntegrationContext
    {
        [Fact]
        public void load_with_batch_larger_than_batch_size_and_overwrite_existing_on_empty_database()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().Duplicate(x => x.Date);
            });

            var data = Target.GenerateRandomData(11).ToArray();

            theStore.BulkInsert(data, BulkInsertMode.OverwriteExisting, batchSize: 10);

            theSession.Query<Target>().Count().ShouldBe(data.Length);
        }


    }
}
