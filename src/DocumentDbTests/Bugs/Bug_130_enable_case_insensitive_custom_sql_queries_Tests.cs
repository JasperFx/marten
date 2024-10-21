using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_130_enable_case_insensitive_custom_sql_queries_Tests: BugIntegrationContext
{
    [Fact]
    public async Task query()
    {
        var entity = new Target();
        theSession.Store(entity);
        await theSession.SaveChangesAsync();

        theSession.Query<Target>($"SELECT data FROM {SchemaName}.mt_doc_target").Single().Id.ShouldBe(entity.Id);
    }
}
