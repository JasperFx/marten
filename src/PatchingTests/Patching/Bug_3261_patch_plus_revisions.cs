using System.Threading.Tasks;
using Marten;
using Marten.Patching;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace PatchingTests.Patching;

public class Bug_3261_patch_plus_revisions : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_3261_patch_plus_revisions(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task can_use_patch_with_revision()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<DocDto>().UseNumericRevisions(true).MultiTenanted();
        });

        var doc = new DocDto { Name = "Jason Tatum" };
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        theSession.Logger = new TestOutputMartenLogger(_output);

        theSession.Patch<DocDto>(d => d.Id == doc.Id && d.AnyTenant()).Set("name", "newvalue");
        await theSession.SaveChangesAsync();

    }
}

public class DocDto
{
    public long Id { get; set; }
    public string Name { get; set; }
    public int Version { get; set; }
}
