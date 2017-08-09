using System.IO;
using System.Linq;
using Marten.Schema;
using Marten.Storage;
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

            new UpsertFunction(theHierarchy).Write(new DdlRules(), writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar) RETURNS bigint LANGUAGE plpgsql SECURITY INVOKER AS $function$");
        }
        

        [Fact]
        public void can_generate_upsert_function_with_security_definer()
        {
            var writer = new StringWriter();

            new UpsertFunction(theHierarchy).Write(new DdlRules
            {
                UpsertRights = SecurityRights.Definer
            }, writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER AS $function$");
        }

        [Fact]
        public void can_generate_upsert_function_for_94()
        {
            var writer = new StringWriter();

            new UpsertFunction(theHierarchy).Write(new DdlRules(), writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar) RETURNS bigint LANGUAGE plpgsql SECURITY INVOKER AS $function$");
        }

        [Fact]
        public void can_generate_upsert_function_for_95_with_optimistic_concurrency()
        {
            var writer = new StringWriter();

            theHierarchy.UseOptimisticConcurrency = true;
            new UpsertFunction(theHierarchy).Write(new DdlRules(), writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(current_version bigint, doc JSONB, docDotNetType varchar, docId varchar, docType varchar) RETURNS bigint LANGUAGE plpgsql SECURITY INVOKER AS $function$");
        }

        [Fact]
        public void can_generate_upsert_function_for_94_with_optimistic_concurrency()
        {
            var writer = new StringWriter();

            theHierarchy.UseOptimisticConcurrency = true;
            new UpsertFunction(theHierarchy).Write(new DdlRules(), writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(current_version bigint, doc JSONB, docDotNetType varchar, docId varchar, docType varchar) RETURNS bigint LANGUAGE plpgsql SECURITY INVOKER AS $function$");
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
		public void contains_index_for_documenttype_column()
		{
		    var table = new DocumentTable(theHierarchy);
            table.Indexes.Any(x => x.IndexName == $"mt_doc_squad_idx_{DocumentMapping.DocumentTypeColumn}").ShouldBeTrue();

		}

		[Fact]
        public void can_generate_upsert_function_for_95()
        {
            var writer = new StringWriter();

            new UpsertFunction(theHierarchy).Write(new DdlRules(), writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar) RETURNS bigint LANGUAGE plpgsql SECURITY INVOKER AS $function$");
        }

        [Fact]
        public void can_generate_upsert_function_for_94()
        {
            var writer = new StringWriter();

            new UpsertFunction(theHierarchy).Write(new DdlRules(), writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar) RETURNS bigint LANGUAGE plpgsql SECURITY INVOKER AS $function$");
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