using System;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_3194_gist_index_on_object_array : BugIntegrationContext
{
    [Fact]
    public async Task can_create_the_index()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<DocWithObjectArray>().Index(x => x.Items, y => y.ToGinWithJsonbPathOps());
        });

        theSession.Store(new DocWithObjectArray{Items = []});
        await theSession.SaveChangesAsync();
    }
}

public class DocWithObjectArray
{
    public Guid Id { get; set; }
    public object[] Items { get; set; }
}
