using System.Linq;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class previewing_the_command_from_a_queryable_Tests : IntegrationContext
    {
        [Fact]
        public void preview_basic_select_command()
        {
            var cmd = theSession.Query<Target>().ToCommand(FetchType.FetchMany);

            cmd.CommandText.ShouldBe("select d.id, d.data, d.mt_version from public.mt_doc_target as d");
            cmd.Parameters.Any().ShouldBeFalse();
        }

        [Fact]
        public void preview_command_with_where_and_parameters()
        {
            var cmd = theSession.Query<Target>().Where(x => x.Number == 3 && x.Double > 2).ToCommand(FetchType.FetchMany);

            cmd.CommandText.ShouldBe("select d.id, d.data, d.mt_version from public.mt_doc_target as d where (CAST(d.data ->> 'Number' as integer) = :p0 and CAST(d.data ->> 'Double' as double precision) > :p1)");

            cmd.Parameters.Count.ShouldBe(2);
            cmd.Parameters["p0"].Value.ShouldBe(3);
            cmd.Parameters["p1"].Value.ShouldBe(2);
        }

        [Fact]
        public void preview_basic_count_command()
        {
            var cmd = theSession.Query<Target>().ToCommand(FetchType.Count);

            cmd.CommandText.ShouldBe("select count(*) as number from public.mt_doc_target as d");
        }

        [Fact]
        public void preview_basic_any_command()
        {
            var cmd = theSession.Query<Target>().ToCommand(FetchType.Any);

            cmd.CommandText.ShouldBe("select (count(*) > 0) as result from public.mt_doc_target as d");
        }

        [Fact]
        public void preview_select_on_query()
        {
            var cmd = theSession.Query<Target>().OrderBy(x => x.Double).ToCommand(FetchType.FetchOne);

            cmd.CommandText.Trim().ShouldBe("select d.id, d.data, d.mt_version from public.mt_doc_target as d order by CAST(d.data ->> 'Double' as double precision) LIMIT :p0");
        }

        [Fact]
        public void preview_collection_any_containment_command()
        {
            var tags = new[] { "ONE", "TWO" };
            var cmd = theSession.Query<Target>().Where(x => x.TagsArray.Any(t => tags.Contains(t))).ToCommand(FetchType.FetchMany);

            cmd.CommandText.ShouldBe("select d.data, d.id, d.mt_version, d.mt_last_modified, d.mt_dotnet_type from public.mt_doc_target as d where CAST(d.data ->> 'TagsArray' as jsonb) ?| :arg0");
            cmd.Parameters["arg0"].Value.ShouldBe(tags);
        }

        public previewing_the_command_from_a_queryable_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }

    public class previewing_the_command_from_a_queryable_inb_different_schema_Tests : IntegrationContext
    {
        public previewing_the_command_from_a_queryable_inb_different_schema_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
            StoreOptions(_ => _.DatabaseSchemaName = "other");
        }

        [Fact]
        public void preview_basic_select_command()
        {
            var cmd = theSession.Query<Target>().ToCommand(FetchType.FetchMany);

            cmd.CommandText.ShouldBe("select d.id, d.data, d.mt_version from other.mt_doc_target as d");
            cmd.Parameters.Any().ShouldBeFalse();
        }

        [Fact]
        public void preview_command_with_where_and_parameters()
        {
            var cmd = theSession.Query<Target>().Where(x => x.Number == 3 && x.Double > 2).ToCommand(FetchType.FetchMany);

            cmd.CommandText.ShouldBe("select d.id, d.data, d.mt_version from other.mt_doc_target as d where (CAST(d.data ->> 'Number' as integer) = :p0 and CAST(d.data ->> 'Double' as double precision) > :p1)");

            cmd.Parameters.Count.ShouldBe(2);
            cmd.Parameters["p0"].Value.ShouldBe(3);
            cmd.Parameters["p1"].Value.ShouldBe(2);
        }

        [Fact]
        public void preview_basic_count_command()
        {
            var cmd = theSession.Query<Target>().ToCommand(FetchType.Count);

            cmd.CommandText.ShouldBe("select count(*) as number from other.mt_doc_target as d");
        }

        [Fact]
        public void preview_basic_any_command()
        {
            var cmd = theSession.Query<Target>().ToCommand(FetchType.Any);

            cmd.CommandText.ShouldBe("select (count(*) > 0) as result from other.mt_doc_target as d");
        }

        [Fact]
        public void preview_select_on_query()
        {
            var cmd = theSession.Query<Target>().OrderBy(x => x.Double).ToCommand(FetchType.FetchOne);

            cmd.CommandText.Trim().ShouldBe("select d.id, d.data, d.mt_version from other.mt_doc_target as d order by CAST(d.data ->> 'Double' as double precision) LIMIT :p0");
        }
    }
}
