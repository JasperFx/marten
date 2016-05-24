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
            theHierarchy = DocumentMapping.For<Squad>();
            theHierarchy.AddSubClass(typeof (BasketballTeam));
            theHierarchy.AddSubClass(typeof (BaseballTeam));
            theHierarchy.AddSubClass(typeof (FootballTeam));
        }

        [Fact]
        public void can_generate_upsert_function_for_95()
        {
            var writer = new StringWriter();

            new UpsertFunction(theHierarchy).WriteFunctionSql(PostgresUpsertType.Standard, writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar, docVersion uuid) RETURNS void LANGUAGE plpgsql AS $function$");
        }

        [Fact]
        public void can_generate_upsert_function_for_94()
        {
            var writer = new StringWriter();

            new UpsertFunction(theHierarchy).WriteFunctionSql(PostgresUpsertType.Legacy, writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar, docVersion uuid) RETURNS void LANGUAGE plpgsql AS $function$");
        }

        [Fact]
        public void can_generate_upsert_function_for_95_with_optimistic_concurrency()
        {
            var writer = new StringWriter();

            theHierarchy.UseOptimisticConcurrency = true;
            new UpsertFunction(theHierarchy).WriteFunctionSql(PostgresUpsertType.Standard, writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(current_version uuid, doc JSONB, docDotNetType varchar, docId varchar, docType varchar, docVersion uuid) RETURNS void LANGUAGE plpgsql AS $function$");
        }

        [Fact]
        public void can_generate_upsert_function_for_94_with_optimistic_concurrency()
        {
            var writer = new StringWriter();

            theHierarchy.UseOptimisticConcurrency = true;
            new UpsertFunction(theHierarchy).WriteFunctionSql(PostgresUpsertType.Legacy, writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(current_version uuid, doc JSONB, docDotNetType varchar, docId varchar, docType varchar, docVersion uuid) RETURNS void LANGUAGE plpgsql AS $function$");
        }

    }

    public class generating_code_and_sql_for_hierarchy_smoke_Tests_on_other_database_schema
    {
        private readonly DocumentMapping theHierarchy;

        public generating_code_and_sql_for_hierarchy_smoke_Tests_on_other_database_schema()
        {
            theHierarchy = DocumentMapping.For<Squad>("other");
            theHierarchy.AddSubClass(typeof(BasketballTeam));
            theHierarchy.AddSubClass(typeof(BaseballTeam));
            theHierarchy.AddSubClass(typeof(FootballTeam));
        }

        [Fact]
        public void can_generate_upsert_function_for_95()
        {
            var writer = new StringWriter();

            new UpsertFunction(theHierarchy).WriteFunctionSql(PostgresUpsertType.Standard, writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar, docVersion uuid) RETURNS void LANGUAGE plpgsql AS $function$");
        }

        [Fact]
        public void can_generate_upsert_function_for_94()
        {
            var writer = new StringWriter();

            new UpsertFunction(theHierarchy).WriteFunctionSql(PostgresUpsertType.Legacy, writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar, docVersion uuid) RETURNS void LANGUAGE plpgsql AS $function$");
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