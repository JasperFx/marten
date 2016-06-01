using Marten.Schema;
using Marten.Schema.BulkLoading;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class bulk_loader_overwrite_sql_tests : IntegratedFixture
    {
        private BulkLoader<Issue> _bulkLoader;

        public bulk_loader_overwrite_sql_tests()
        {
            _bulkLoader = new BulkLoader<Issue>(theStore.Advanced.Serializer, DocumentMapping.For<Issue>(), null);
        }

        [Fact]
        public void Should_generate_overwrite_update_sql_statement()
        {
            var sql = _bulkLoader.OverwriteDuplicatesFromTempTable();

            sql.ShouldBe(@"update public.mt_doc_issue target SET data = source.data, mt_last_modified = source.mt_last_modified, mt_version = source.mt_version, mt_dotnet_type = source.mt_dotnet_type FROM mt_doc_issue_temp source WHERE source.id = target.id");
        }
    }
}