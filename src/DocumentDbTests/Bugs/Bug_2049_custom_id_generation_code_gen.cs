using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Testing.OtherAssembly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_2049_custom_id_generation_code_gen : BugIntegrationContext
{


    [Fact]
    public async Task can_use_custom_codegen()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<StringDoc>().IdStrategy(new String2IdGeneration());
        });

        var doc = new StringDoc();
        theSession.Store(doc);
        await theSession.SaveChangesAsync();
    }
}
