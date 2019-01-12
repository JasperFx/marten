using System.Linq;
using Marten.Testing.Documents;
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

            using (var session = store.OpenSession())
            {
                session.Store(new User { FirstName = "Jeremy", LastName = "Miller", UserName = "jmiller" });
                session.Store(new User { FirstName = "Lindsey", LastName = "Miller", UserName = "lmiller" });
                session.Store(new User { FirstName = "Max", LastName = "Miller", UserName = "mmiller" });
                session.Store(new User { FirstName = "Frank", LastName = "Zombo", UserName = "fzombo" });
                session.Store(new User { FirstName = "Somebody", LastName = "Somewher", UserName = "somebody" });
                session.SaveChanges();

                var somebody = session.Search<User>("somebody");
            }

            store.Dispose();

            // ENDSAMPLE
        }

        [Fact]
        public void creating_a_full_text_index_should_create_the_index_on_the_table()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex());

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                              .Where(x => x.Name == "mt_doc_target_english_idx_fts")
                              .Select(x => x.DDL.ToLower())
                              .First();

            ddl.ShouldContain("create index mt_doc_target_english_idx_fts");
            ddl.ShouldContain("on public.mt_doc_target");
            ddl.ShouldContain("to_tsvector");
        }

        [Fact]
        public void not_specifying_an_index_name_should_generate_default_index_name()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex());
            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                              .Select(x => x.DDL.ToLower())
                              .First();

            ddl.ShouldContain("mt_doc_target_english_idx_fts");
        }

        [Fact]
        public void specifying_an_index_name_without_marten_prefix_should_prepend_prefix()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(configure: x => x.IndexName = "doesnt_have_prefix"));
            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                              .Select(x => x.DDL)
                              .First();

            ddl.ShouldContain("mt_doesnt_have_prefix");
        }

        [Fact]
        public void specifying_an_index_name_with_mixed_case_should_result_in_lower_case_name()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(configure: x => x.IndexName = "Doesnt_Have_PreFix"));
            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                              .Select(x => x.DDL)
                              .First();

            ddl.ShouldContain("mt_doesnt_have_prefix");
        }

        [Fact]
        public void specifying_an_index_name_with_marten_prefix_remains_unchanged()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(configure: x => x.IndexName = "mt_i_have_prefix"));
            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                              .Select(x => x.DDL)
                              .First();

            ddl.ShouldContain("mt_i_have_prefix");
        }

        [Fact]
        public void specifying_an_index_name_with_marten_prefix_and_mixed_case_results_in_lowercase_name()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(configure: x => x.IndexName = "mT_I_hAve_preFIX"));
            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                              .Select(x => x.DDL)
                              .First();

            ddl.ShouldContain("mt_i_have_prefix");
        }
    }
}