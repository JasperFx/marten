using System;
using System.Linq;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class full_text_index : IntegratedFixture
    {
        [Fact]
        public void example()
        {
            // SAMPLE: using-a-full-text-index
            var store = DocumentStore.For(_ =>
                                          {
                                              _.Connection(ConnectionSource.ConnectionString);

                                              // Create the full text index
                                              _.Schema.For<User>().FullTextIndex();
                                          });

            using(var session = store.QuerySession())
            {
                var somebody = session.Search<User>("somebody");
            }

            store.Dispose();

            // ENDSAMPLE
        }

        [Fact]
        public void creating_a_full_text_index_should_create_the_index_on_the_table()
        {
            StoreOptions(_=>_.Schema.For<Target>().FullTextIndex());

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                              .Where(x => x.Name == "mt_target_idx_fts")
                              .Select(x => x.DDL.ToLower())
                              .First();

            ddl.ShouldBe("CREATE INDEX mt_target_idx_fts ON mt_doc_target USING gin (( to_tsvector('english', data) ));");
        }
    }
}