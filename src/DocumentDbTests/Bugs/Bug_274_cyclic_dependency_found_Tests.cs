using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_274_cyclic_dependency_found_Tests: BugIntegrationContext
{
    [Fact]
    public async Task save()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Issue>()
                .ForeignKey<User>(x => x.AssigneeId)
                .ForeignKey<User>(x => x.ReporterId);
        });

        theSession.Store(new Issue());
        await theSession.SaveChangesAsync();
    }

}
