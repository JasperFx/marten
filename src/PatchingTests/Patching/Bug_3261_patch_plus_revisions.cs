using System.Threading.Tasks;
using Marten;
using Marten.Patching;
using Marten.Testing.Harness;
using Xunit;

namespace PatchingTests.Patching;

public class Bug_3261_patch_plus_revisions : BugIntegrationContext
{
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
