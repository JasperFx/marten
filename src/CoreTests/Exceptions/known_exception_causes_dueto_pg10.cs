using System.Linq;
using CoreTests.Harness;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace CoreTests.Exceptions
{
    public class known_exception_causes_dueto_pg10: IntegrationContext
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
            SpecificationExtensions.ShouldContain(e.Message, KnownNotSupportedExceptionCause.WebStyleSearch.Description);
        }

        public known_exception_causes_dueto_pg10(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
