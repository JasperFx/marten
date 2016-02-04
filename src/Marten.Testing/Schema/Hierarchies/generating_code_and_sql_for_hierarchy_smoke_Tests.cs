using System.Diagnostics;
using System.IO;
using Marten.Schema;
using Marten.Schema.Hierarchies;
using Xunit;

namespace Marten.Testing.Schema.Hierarchies
{
    public class generating_code_and_sql_for_hierarchy_smoke_Tests
    {
        private readonly HierarchyMapping theHierarchy;

        public generating_code_and_sql_for_hierarchy_smoke_Tests()
        {
            theHierarchy = new HierarchyMapping(typeof(Squad), new StoreOptions());
            theHierarchy.AddSubClass(typeof (BasketballTeam));
            theHierarchy.AddSubClass(typeof (BaseballTeam));
            theHierarchy.AddSubClass(typeof (FootballTeam));

        }

        [Fact]
        public void can_generate_upsert_function_for_95()
        {
            var writer = new StringWriter();

            theHierarchy.ToUpsertFunction().WriteFunctionSql(PostgresUpsertType.Standard, writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION mt_upsert_squad(docId varchar, doc JSONB, docType varchar) RETURNS VOID AS");
            sql.ShouldContain("DO UPDATE SET \"data\" = doc, \"mt_doc_type\" = docType;");
        }

        [Fact]
        public void can_generate_upsert_function_for_94()
        {
            var writer = new StringWriter();

            theHierarchy.ToUpsertFunction().WriteFunctionSql(PostgresUpsertType.Legacy, writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION mt_upsert_squad(docId varchar, doc JSONB, docType varchar) RETURNS VOID AS");
            sql.ShouldContain("WITH upsert AS (UPDATE mt_doc_squad SET \"data\" = doc, \"mt_doc_type\" = docType WHERE id=docId RETURNING *)");
        }

        [Fact]
        public void generate_document_storage_code_for_the_hierarchy_without_blowing_up()
        {
            //DocumentStorageBuilder.Build(null, theHierarchy).ShouldNotBeNull();
        }
    }

    public class Squad
    {
        public string Id { get; set; }
    }

    public class BasketballTeam : Squad { }
    public class FootballTeam : Squad { }
    public class BaseballTeam : Squad { }
}