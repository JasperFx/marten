#nullable enable
using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_2942_include_error : BugIntegrationContext
{
    public record DocumentA(Guid Id, string DetailA);
    public record DocumentB(Guid Id, string DetailB);

    private readonly Guid THEID = Guid.NewGuid();

    [Fact]
    public async Task query_with_include()
    {
        var id = Guid.NewGuid();

        DocumentB? docB = null;
        await theSession.Query<DocumentA>().Include<DocumentB>(a => a.Id, b => docB = b).SingleOrDefaultAsync(a => a.Id == THEID);
        await theSession.Query<DocumentA>().Include<DocumentB>(a => a.Id, b => docB = b).SingleOrDefaultAsync(a => a.Id == id);

    }
}
