using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Util;
using Xunit;

namespace CoreTests;

public class enhance_polly_smoke_test : OneOffConfigurationsContext
{
    [Fact]
    public async Task smoke_test()
    {
        StoreOptions(opts =>
        {
            opts.ExtendPolly(builder =>
            {
                builder.AddMartenDefaults();
            });
        });

        theSession.Store(Target.Random());
        await theSession.SaveChangesAsync();
    }
}
