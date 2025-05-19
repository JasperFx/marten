using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Exceptions;

public class known_exception_causes_dueto_pg9: IntegrationContext
{
    [PgVersionTargetedFact(MaximumVersion = "10.0")]
    public void can_map_jsonb_FTS_not_supported()
    {
        var e = Assert.Throws<MartenCommandNotSupportedException>(() =>
        {
            using var session = theStore.QuerySession();
            session.Query<User>().Where(x => x.PlainTextSearch("throw")).ToList();
        });

        e.Reason.ShouldBe(NotSupportedReason.FullTextSearchNeedsAtLeastPostgresVersion10);
        e.Message.ShouldContain(KnownNotSupportedExceptionCause.ToTsvectorOnJsonb.Description);
    }

    [PgVersionTargetedFact(MaximumVersion = "10.0")]
    public async Task can_totsvector_other_than_jsonb_without_FTS_exception()
    {
        var e = await Should.ThrowAsync<MartenCommandException>(async () =>
        {
            using var session = theStore.QuerySession();
            await session.QueryAsync<User>("to_tsvector(?)", 0);
        });

        e.ShouldNotBeOfType<MartenCommandNotSupportedException>();
    }

    public known_exception_causes_dueto_pg9(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
