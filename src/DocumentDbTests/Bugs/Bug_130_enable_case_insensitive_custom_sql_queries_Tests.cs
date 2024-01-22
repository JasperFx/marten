using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_130_enable_case_insensitive_custom_sql_queries_Tests: BugIntegrationContext
{
    [Fact]
    public void query()
    {
        var entity = new Target();
        TheSession.Store(entity);
        TheSession.SaveChanges();

        TheSession.Query<Target>($"SELECT data FROM {SchemaName}.mt_doc_target").Single().Id.ShouldBe(entity.Id);
    }
}
