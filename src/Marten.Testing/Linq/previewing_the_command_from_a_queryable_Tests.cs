using System.Linq;
using Marten.Linq;
using Marten.Services;
using Marten.Testing.Fixtures;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class previewing_the_command_from_a_queryable_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void preview_basic_select_command()
        {
            var cmd = theSession.Query<Target>().ToCommand(FetchType.FetchMany);

            cmd.CommandText.ShouldBe("select d.data, d.id from mt_doc_target as d");
            cmd.Parameters.Any().ShouldBeFalse();
        }

        [Fact]
        public void preview_command_with_where_and_parameters()
        {
            var cmd = theSession.Query<Target>().Where(x => x.Number == 3 && x.Double > 2).ToCommand(FetchType.FetchMany);

            cmd.CommandText.ShouldBe("select d.data, d.id from mt_doc_target as d where (CAST(d.data ->> 'Number' as integer) = :arg0) and (CAST(d.data ->> 'Double' as double precision) > :arg1)");


            cmd.Parameters.Count.ShouldBe(2);
            cmd.Parameters["arg0"].Value.ShouldBe(3);
            cmd.Parameters["arg1"].Value.ShouldBe(2);
        }

        [Fact]
        public void preview_basic_count_command()
        {
            var cmd = theSession.Query<Target>().ToCommand(FetchType.Count);

            cmd.CommandText.ShouldBe("select count(*) as number from mt_doc_target as d");
        }

        [Fact]
        public void preview_basic_any_command()
        {
            var cmd = theSession.Query<Target>().ToCommand(FetchType.Any);

            cmd.CommandText.ShouldBe("select (count(*) > 0) as result from mt_doc_target as d");
        }

        [Fact]
        public void preview_select_on_query()
        {
            var cmd = theSession.Query<Target>().OrderBy(x => x.Double).ToCommand(FetchType.FetchOne);

            cmd.CommandText.Trim().ShouldBe("select d.data, d.id from mt_doc_target as d order by CAST(d.data ->> 'Double' as double precision) LIMIT 1");
        }
    }
}