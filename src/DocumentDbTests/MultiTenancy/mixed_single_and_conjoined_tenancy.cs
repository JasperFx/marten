using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.MultiTenancy;

public class mixed_single_and_conjoined_tenancy : OneOffConfigurationsContext
{
    public mixed_single_and_conjoined_tenancy()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().SingleTenanted();
            opts.Schema.For<User>().MultiTenanted();
        });
    }

    [Fact]
    public async Task load_single_tenanted_document_from_tenanted_session()
    {
        var targets = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertDocumentsAsync(targets);

        using var session1 = theStore.LightweightSession("blue");
        (await session1.LoadAsync<Target>(targets[0].Id)).ShouldNotBeNull();

        // Now do nested
        var session2 = theSession.ForTenant("green");
        (await session2.LoadAsync<Target>(targets[1].Id)).ShouldNotBeNull();
    }
}
