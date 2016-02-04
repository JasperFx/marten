using System.Linq;
using Shouldly;
using Xunit;
using Marten.Services;
using Marten.Testing.Fixtures;

namespace Marten.Testing.Bugs
{
    public class Bug_130_enable_case_insensitive_custom_sql_queries_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void query()
        {
            var entity = new Target();
            theSession.Store(entity);
            theSession.SaveChanges();

            theSession.Query<Target>("SELECT data FROM mt_doc_target").Single().Id.ShouldBe(entity.Id);
        }
    }
}