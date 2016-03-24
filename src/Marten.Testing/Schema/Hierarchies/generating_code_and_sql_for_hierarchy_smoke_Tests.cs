using System.IO;
using Marten.Schema;
using Xunit;

namespace Marten.Testing.Schema.Hierarchies
{
    public class generating_code_and_sql_for_hierarchy_smoke_Tests
    {
        private readonly DocumentMapping theHierarchy;

        public generating_code_and_sql_for_hierarchy_smoke_Tests()
        {
            theHierarchy = DocumentMappingFactory.For<Squad>();
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

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(doc JSONB, docId varchar, docType varchar) RETURNS VOID AS");
            sql.ShouldContain("DO UPDATE SET \"data\" = doc, \"mt_doc_type\" = docType;");
        }

        [Fact]
        public void can_generate_upsert_function_for_94()
        {
            var writer = new StringWriter();

            theHierarchy.ToUpsertFunction().WriteFunctionSql(PostgresUpsertType.Legacy, writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(doc JSONB, docId varchar, docType varchar) RETURNS VOID AS");
            sql.ShouldContain("WITH upsert AS (UPDATE public.mt_doc_squad SET \"data\" = doc, \"mt_doc_type\" = docType WHERE id=docId RETURNING *)");
        }

        [Fact]
        public void generate_document_storage_code_for_the_hierarchy_without_blowing_up()
        {
            DocumentStorageBuilder.Build(null, theHierarchy).ShouldNotBeNull();
        }
    }

    public class generating_code_and_sql_for_hierarchy_smoke_Tests_on_other_database_schema
    {
        private readonly DocumentMapping theHierarchy;

        public generating_code_and_sql_for_hierarchy_smoke_Tests_on_other_database_schema()
        {
            theHierarchy = DocumentMappingFactory.For<Squad>("other");
            theHierarchy.AddSubClass(typeof(BasketballTeam));
            theHierarchy.AddSubClass(typeof(BaseballTeam));
            theHierarchy.AddSubClass(typeof(FootballTeam));
        }

        [Fact]
        public void can_generate_upsert_function_for_95()
        {
            var writer = new StringWriter();

            theHierarchy.ToUpsertFunction().WriteFunctionSql(PostgresUpsertType.Standard, writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_squad(doc JSONB, docId varchar, docType varchar) RETURNS VOID AS");
            sql.ShouldContain("DO UPDATE SET \"data\" = doc, \"mt_doc_type\" = docType;");
        }

        [Fact]
        public void can_generate_upsert_function_for_94()
        {
            var writer = new StringWriter();

            theHierarchy.ToUpsertFunction().WriteFunctionSql(PostgresUpsertType.Legacy, writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_squad(doc JSONB, docId varchar, docType varchar) RETURNS VOID AS");
            sql.ShouldContain("WITH upsert AS (UPDATE other.mt_doc_squad SET \"data\" = doc, \"mt_doc_type\" = docType WHERE id=docId RETURNING *)");
        }

        [Fact]
        public void generate_document_storage_code_for_the_hierarchy_without_blowing_up()
        {
            DocumentStorageBuilder.Build(null, theHierarchy).ShouldNotBeNull();
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