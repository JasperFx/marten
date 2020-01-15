using System.Linq;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Exceptions
{
    public class known_exception_causes_dueto_pg10: IntegratedFixture
    {
        [PgVersionTargetedFact(MinimumVersion = "10.0", MaximumVersion = "11.0")]
        public void can_map_web_style_search_not_supported()
        {
            var e = Assert.Throws<MartenCommandNotSupportedException>(() =>
            {
                using (var session = theStore.OpenSession())
                {
                    session.Query<User>().Where(x => x.WebStyleSearch("throw")).ToList();
                }
            });

            e.Reason.ShouldBe(NotSupportedReason.WebStyleSearchNeedsAtLeastPostgresVersion11);
            e.Message.ShouldContain(KnownNotSupportedExceptionCause.WebStyleSearch.Description);
        }
    }
}
