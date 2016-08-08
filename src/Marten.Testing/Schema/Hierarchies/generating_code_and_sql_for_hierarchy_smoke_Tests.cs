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

            new UpsertFunction(theHierarchy).WriteFunctionSql(new DdlRules(), writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar, docVersion uuid) RETURNS UUID LANGUAGE plpgsql SECURITY INVOKER AS $function$");
        }

        [Fact]
        public void can_generate_upsert_function_with_grants()
        {
            var writer = new StringWriter();

            var rules = new DdlRules();
            rules.GrantToRoles.Add("foo");
            rules.GrantToRoles.Add("bar");

            new UpsertFunction(theHierarchy).WriteFunctionSql(rules, writer);

            var sql = writer.ToString();

            sql.ShouldContain("GRANT EXECUTE ON public.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar, docVersion uuid) TO \"foo\";");
            sql.ShouldContain("GRANT EXECUTE ON public.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar, docVersion uuid) TO \"bar\";");
        }

        [Fact]
        public void can_generate_upsert_function_with_security_definer()
        {
            var writer = new StringWriter();

            new UpsertFunction(theHierarchy).WriteFunctionSql(new DdlRules
            {
                UpsertRights = SecurityRights.Definer
            }, writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar, docVersion uuid) RETURNS UUID LANGUAGE plpgsql SECURITY DEFINER AS $function$");
        }

        [Fact]
        public void can_generate_upsert_function_for_94()
        {
            var writer = new StringWriter();

            new UpsertFunction(theHierarchy).WriteFunctionSql(new DdlRules(), writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar, docVersion uuid) RETURNS UUID LANGUAGE plpgsql SECURITY INVOKER AS $function$");
        }

        [Fact]
        public void can_generate_upsert_function_for_95_with_optimistic_concurrency()
        {
            var writer = new StringWriter();

            theHierarchy.UseOptimisticConcurrency = true;
            new UpsertFunction(theHierarchy).WriteFunctionSql(new DdlRules(), writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(current_version uuid, doc JSONB, docDotNetType varchar, docId varchar, docType varchar, docVersion uuid) RETURNS UUID LANGUAGE plpgsql SECURITY INVOKER AS $function$");
        }

        [Fact]
        public void can_generate_upsert_function_for_94_with_optimistic_concurrency()
        {
            var writer = new StringWriter();

            theHierarchy.UseOptimisticConcurrency = true;
            new UpsertFunction(theHierarchy).WriteFunctionSql(new DdlRules(), writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_squad(current_version uuid, doc JSONB, docDotNetType varchar, docId varchar, docType varchar, docVersion uuid) RETURNS UUID LANGUAGE plpgsql SECURITY INVOKER AS $function$");
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

            new UpsertFunction(theHierarchy).WriteFunctionSql(new DdlRules(), writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar, docVersion uuid) RETURNS UUID LANGUAGE plpgsql SECURITY INVOKER AS $function$");
        }

        [Fact]
        public void can_generate_upsert_function_for_94()
        {
            var writer = new StringWriter();

            new UpsertFunction(theHierarchy).WriteFunctionSql(new DdlRules(), writer);

            var sql = writer.ToString();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_squad(doc JSONB, docDotNetType varchar, docId varchar, docType varchar, docVersion uuid) RETURNS UUID LANGUAGE plpgsql SECURITY INVOKER AS $function$");
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