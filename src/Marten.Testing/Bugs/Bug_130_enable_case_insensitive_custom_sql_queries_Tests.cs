using System.Linq;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    [Collection("DefaultSchema")]
    public class Bug_130_enable_case_insensitive_custom_sql_queries_Tests: DocumentSessionFixture<NulloIdentityMap>
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
