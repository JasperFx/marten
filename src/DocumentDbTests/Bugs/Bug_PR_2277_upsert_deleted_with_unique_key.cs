using System;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs
{
    public class Bug_PR_2277_upsert_deleted_with_unique_key: IntegrationContext
    {
        public Bug_PR_2277_upsert_deleted_with_unique_key(DefaultStoreFixture fixture): base(fixture)
        {
        }

        [Fact]
        public async Task can_upsert_deleted_record_with_unique_key()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<RecordA>().Duplicate(r => r.UniqueKey, configure: c => c.IsUnique = true);
            });

            var recordA = new RecordA { Id = Guid.NewGuid(), UniqueKey = "test123" };
            var recordB = new RecordB { Id = Guid.NewGuid(), UniqueKey = "test123" };
            await theStore.BulkInsertAsync(new[] { recordA });
            await theStore.BulkInsertAsync(new[] { recordB });

            theSession.Delete(recordA);
            theSession.Delete(recordB);
            theSession.Store(new RecordA { Id = Guid.NewGuid(), UniqueKey = "test123" });
            theSession.Store(new RecordB { Id = Guid.NewGuid(), UniqueKey = "test123" });

            await theSession.SaveChangesAsync();
        }

        public class RecordA
        {
            public Guid Id { get; set; }
            public string UniqueKey { get; set; }
        }

        public class RecordB
        {
            public Guid Id { get; set; }
            public string UniqueKey { get; set; }
        }
    }
}
